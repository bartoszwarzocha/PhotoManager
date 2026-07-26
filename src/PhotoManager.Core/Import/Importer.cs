using System.Globalization;
using PhotoManager.Core.Metadata;

namespace PhotoManager.Core.Import;

/// <summary>
/// Silnik importu zdjęć z nośnika do biblioteki docelowej: organizacja wg daty EXIF,
/// deduplikacja po skrócie zawartości (manifest) oraz bezpieczne przenoszenie
/// (kasowanie źródła dopiero po zweryfikowanej kopii).
/// </summary>
public sealed class Importer
{
    /// <summary>Importuje wszystkie pasujące pliki z <paramref name="sourceRoot"/> zgodnie z <paramref name="options"/>.</summary>
    public Task<ImportReport> ImportAsync(
        string sourceRoot,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var files = EnumeratePhotos(sourceRoot, options.Extensions).ToList();
        return ImportFilesAsync(files, options, progress, ct);
    }

    /// <summary>
    /// Importuje konkretną listę plików (np. zaznaczonych w oknie podglądu). Z <c>DryRun=true</c>
    /// działa jako analiza — zwraca decyzje „nowy/duplikat” bez ruszania plików (do podglądu).
    /// </summary>
    public async Task<ImportReport> ImportFilesAsync(
        IReadOnlyList<string> files,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var report = new ImportReport();
        var manifest = ImportManifest.Load(options.DestinationRoot);
        int index = 0;

        try
        {
            foreach (var source in files)
            {
                ct.ThrowIfCancellationRequested();
                index++;
                ImportItemResult result;
                try
                {
                    result = await ProcessFileAsync(source, options, manifest, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result = new ImportItemResult(source, ImportOutcome.Failed, Message: ex.Message);
                }

                report.Items.Add(result);
                if (result.Outcome == ImportOutcome.Imported && result.TargetPath is not null)
                {
                    try { report.BytesImported += new FileInfo(result.TargetPath).Length; } catch { }
                }

                progress?.Report(new ImportProgress(index, files.Count, source, result.Outcome));
            }
        }
        finally
        {
            // Zapis także przy przerwaniu — to, co zdążyliśmy zaimportować, ma zostać zapamiętane.
            if (!options.DryRun)
                manifest.Save();
        }

        return report;
    }

    /// <summary>
    /// Szybka analiza „nowy/duplikat” DO PODGLĄDU — bez kopiowania i (w typowym przypadku)
    /// bez czytania zawartości plików. Opiera się na indeksie rozmiaru+nazwy w manifeście;
    /// skrót liczy tylko przy rzadkiej kolizji rozmiaru z inną nazwą. W pełni anulowalna.
    /// </summary>
    public async Task<List<ImportItemResult>> AnalyzeAsync(
        IReadOnlyList<string> files,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        var manifest = ImportManifest.Load(options.DestinationRoot);
        var results = new List<ImportItemResult>(files.Count);
        int index = 0;

        foreach (var source in files)
        {
            ct.ThrowIfCancellationRequested();
            index++;

            long size;
            try { size = new FileInfo(source).Length; } catch { size = -1; }
            var name = Path.GetFileName(source);

            var decision = size < 0 ? DedupDecision.New : manifest.FastCheck(name, size);
            var outcome = decision switch
            {
                DedupDecision.Duplicate => ImportOutcome.SkippedDuplicate,
                DedupDecision.NeedsHash =>
                    manifest.Contains(await FileHasher.ComputeAsync(source, ct))
                        ? ImportOutcome.SkippedDuplicate : ImportOutcome.Imported,
                _ => ImportOutcome.Imported,
            };

            results.Add(new ImportItemResult(source, outcome));
            progress?.Report(new ImportProgress(index, files.Count, source, outcome));
        }

        return results;
    }

    private static async Task<ImportItemResult> ProcessFileAsync(
        string source, ImportOptions options, ImportManifest manifest, CancellationToken ct)
    {
        var fileName = Path.GetFileName(source);
        long size;
        try { size = new FileInfo(source).Length; } catch { size = -1; }

        // 1) Szybka deduplikacja bez czytania zawartości (rozmiar+nazwa). Skrót tylko przy kolizji.
        if (size >= 0)
        {
            var decision = manifest.FastCheck(fileName, size);
            if (decision == DedupDecision.Duplicate)
                return new ImportItemResult(source, ImportOutcome.SkippedDuplicate, Message: "duplikat (rozmiar+nazwa)");
            if (decision == DedupDecision.NeedsHash)
            {
                var h = await FileHasher.ComputeAsync(source, ct);
                if (manifest.Contains(h))
                    return new ImportItemResult(source, ImportOutcome.SkippedDuplicate, Message: "duplikat (skrót)");
            }
        }

        // 2) Ustal folder docelowy z daty wykonania.
        var captureDate = PhotoMetadata.GetCaptureDate(source);
        var subDir = BuildSubDir(captureDate, options.FolderPattern);
        var targetDir = Path.Combine(options.DestinationRoot, subDir);
        var targetPath = Path.Combine(targetDir, fileName);

        // Tryb próbny: zgłoś decyzję bez dotykania dysku. Dopisujemy do manifestu w pamięci,
        // żeby wykryć też duplikaty w obrębie tej samej partii (klucz = ścieżka źródła).
        if (options.DryRun)
        {
            var wouldBe = File.Exists(targetPath) ? MakeUniquePath(targetDir, fileName) : targetPath;
            if (size >= 0)
                manifest.Add(source, new ManifestEntry(source, size, captureDate));
            return new ImportItemResult(source, ImportOutcome.Imported, wouldBe, "próbny");
        }

        System.IO.Directory.CreateDirectory(targetDir);

        // 3) Kolizja nazwy w celu: ten sam rozmiar traktujemy jak duplikat, inny → unikalna nazwa.
        if (File.Exists(targetPath))
        {
            long existing;
            try { existing = new FileInfo(targetPath).Length; } catch { existing = -1; }
            if (existing == size)
                return new ImportItemResult(source, ImportOutcome.SkippedDuplicate, targetPath,
                    "już w bibliotece (plik istnieje)");
            targetPath = MakeUniquePath(targetDir, fileName);
        }

        // 4) Kopiuj i policz skrót w jednym przebiegu (jeden odczyt źródła).
        var partPath = targetPath + ".part";
        string sourceHash;
        try
        {
            sourceHash = await FileHasher.CopyAndHashAsync(source, partPath, ct);
        }
        catch (OperationCanceledException)
        {
            TryDelete(partPath);
            throw;
        }

        // 5) Weryfikacja (wymuszona przy przenoszeniu — nie kasujemy źródła bez pewnej kopii).
        bool verify = options.VerifyAfterCopy || options.Mode == ImportMode.Move;
        if (verify)
        {
            var copyHash = await FileHasher.ComputeAsync(partPath, ct);
            if (copyHash != sourceHash)
            {
                TryDelete(partPath);
                return new ImportItemResult(source, ImportOutcome.Failed, Message: "weryfikacja kopii nie powiodła się");
            }
        }

        File.Move(partPath, targetPath, overwrite: false);

        // 6) Przy przenoszeniu skasuj źródło — dopiero gdy kopia jest pewna.
        if (options.Mode == ImportMode.Move)
            TryDelete(source);

        AddToManifest(manifest, options.DestinationRoot, targetPath, sourceHash, captureDate);
        return new ImportItemResult(source, ImportOutcome.Imported, targetPath);
    }

    private static void AddToManifest(
        ImportManifest manifest, string destRoot, string targetPath, string hash, DateTime date)
    {
        long size = 0;
        try { size = new FileInfo(targetPath).Length; } catch { }
        var rel = Path.GetRelativePath(destRoot, targetPath);
        manifest.Add(hash, new ManifestEntry(rel, size, date));
    }

    /// <summary>Buduje ścieżkę podfolderów z daty; „/” we wzorcu = zagnieżdżenie.</summary>
    private static string BuildSubDir(DateTime date, string pattern)
    {
        var rendered = date.ToString(pattern, CultureInfo.InvariantCulture);
        var parts = rendered.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine(parts);
    }

    private static string MakeUniquePath(string dir, string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 1; ; i++)
        {
            var candidate = Path.Combine(dir, $"{stem}_{i}{ext}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    /// <summary>Zwraca wszystkie pliki pod <paramref name="root"/> pasujące do zestawu rozszerzeń.</summary>
    public static IEnumerable<string> EnumeratePhotos(string root, IReadOnlySet<string> extensions)
    {
        IEnumerable<string> all;
        try
        {
            all = System.IO.Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (var path in all)
        {
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext) && extensions.Contains(ext))
                yield return path;
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
