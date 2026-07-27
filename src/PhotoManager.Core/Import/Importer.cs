using System.Globalization;
using PhotoManager.Core.Metadata;

namespace PhotoManager.Core.Import;

/// <summary>
/// Silnik importu zdjęć z nośnika do biblioteki docelowej: organizacja wg daty EXIF,
/// deduplikacja przez porównanie z fizyczną biblioteką (per plik, bez rejestru) oraz
/// bezpieczne przenoszenie (kasowanie źródła dopiero po zweryfikowanej kopii).
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
        int index = 0;

        foreach (var source in files)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            ImportItemResult result;
            try
            {
                result = await ProcessFileAsync(source, options, ct);
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

        return report;
    }

    /// <summary>
    /// Analiza „nowy/duplikat” DO PODGLĄDU — PER PLIK, przez porównanie z FIZYCZNĄ biblioteką.
    /// Dla każdego pliku ustala jego miejsce w bibliotece (folder wg daty + nazwa) i sprawdza,
    /// czy taki plik tam już leży (ten sam rozmiar). Brak w bibliotece = nowy, choćby był zgrywany
    /// wcześniej (bo mógł zostać skasowany). Bez rejestru/manifestu. W pełni anulowalna.
    /// </summary>
    public Task<List<ImportItemResult>> AnalyzeAsync(
        IReadOnlyList<string> files,
        ImportOptions options,
        IProgress<ImportProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Poza wątkiem UI — czyta datę EXIF każdego pliku (Progress marshaluje zgłoszenia z powrotem).
        return Task.Run(() =>
        {
            var results = new List<ImportItemResult>(files.Count);
            int index = 0;

            foreach (var source in files)
            {
                ct.ThrowIfCancellationRequested();
                index++;

                var (targetDir, fileName, size) = ResolveTarget(source, options);
                var outcome = IsSameFilePresentAsync(source, targetDir, fileName, size, options, ct).GetAwaiter().GetResult()
                    ? ImportOutcome.SkippedDuplicate
                    : ImportOutcome.Imported;

                results.Add(new ImportItemResult(source, outcome));
                progress?.Report(new ImportProgress(index, files.Count, source, outcome));
            }

            return results;
        }, ct);
    }

    /// <summary>Ustala docelowy folder (wg daty), nazwę i rozmiar pliku źródłowego.</summary>
    private static (string TargetDir, string FileName, long Size) ResolveTarget(string source, ImportOptions options)
    {
        long size;
        try { size = new FileInfo(source).Length; } catch { size = -1; }
        var date = PhotoMetadata.GetCaptureDate(source);
        var targetDir = Path.Combine(options.DestinationRoot, BuildSubDir(date, options.FolderPattern));
        return (targetDir, Path.GetFileName(source), size);
    }

    /// <summary>
    /// Czy w folderze docelowym leży już ten sam plik: o tej nazwie (lub jej wariancie „_N" z kolizji)
    /// i tym samym rozmiarze. Właściwe porównanie „aparat ↔ biblioteka”, per plik. Gdy włączona
    /// <see cref="ImportOptions.VerifyDuplicateContent"/>, dopasowanie rozmiaru potwierdzane jest
    /// skrótem — plik uszkodzony/inny (ta sama nazwa+rozmiar, inna zawartość) NIE jest duplikatem.
    /// </summary>
    private static async Task<bool> IsSameFilePresentAsync(
        string source, string targetDir, string fileName, long size, ImportOptions options, CancellationToken ct)
    {
        if (size < 0 || !Directory.Exists(targetDir)) return false;

        // Kandydaci: dokładna nazwa + warianty „_N" z wcześniejszych kolizji, o tym samym rozmiarze.
        var candidates = new List<string>();
        var exact = Path.Combine(targetDir, fileName);
        if (SameSize(exact, size)) candidates.Add(exact);

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        try
        {
            foreach (var c in Directory.EnumerateFiles(targetDir, $"{stem}_*{ext}"))
                if (SameSize(c, size)) candidates.Add(c);
        }
        catch { /* brak dostępu — traktujemy jak brak kandydatów */ }

        if (candidates.Count == 0) return false;
        if (!options.VerifyDuplicateContent) return true; // sam rozmiar wystarcza

        // Potwierdzenie zawartością: źródło musi być identyczne z którymś kandydatem.
        var sourceHash = await FileHasher.ComputeAsync(source, ct);
        foreach (var c in candidates)
        {
            try { if (await FileHasher.ComputeAsync(c, ct) == sourceHash) return true; }
            catch { /* nieczytelny kandydat — pomijamy */ }
        }
        return false; // rozmiar się zgadza, ale zawartość różna → uszkodzony/inny, zgraj dobry
    }

    private static bool SameSize(string path, long size)
    {
        try { return File.Exists(path) && new FileInfo(path).Length == size; } catch { return false; }
    }

    private static async Task<ImportItemResult> ProcessFileAsync(
        string source, ImportOptions options, CancellationToken ct)
    {
        var (targetDir, fileName, size) = ResolveTarget(source, options);
        var targetPath = Path.Combine(targetDir, fileName);

        // Deduplikacja PER PLIK względem fizycznej biblioteki (nazwa/wariant + rozmiar; opcjonalnie zawartość).
        if (await IsSameFilePresentAsync(source, targetDir, fileName, size, options, ct))
            return new ImportItemResult(source, ImportOutcome.SkippedDuplicate, targetPath, "już w bibliotece");

        // Tryb próbny: tylko zgłoś decyzję, bez dotykania dysku.
        if (options.DryRun)
        {
            var wouldBe = File.Exists(targetPath) ? MakeUniquePath(targetDir, fileName) : targetPath;
            return new ImportItemResult(source, ImportOutcome.Imported, wouldBe, "próbny");
        }

        System.IO.Directory.CreateDirectory(targetDir);

        // Ta sama nazwa, inny rozmiar (inne zdjęcie z drugiej karty) → unikalna nazwa, oba zostają.
        if (File.Exists(targetPath))
            targetPath = MakeUniquePath(targetDir, fileName);

        // Kopiuj i policz skrót w jednym przebiegu (skrót do ewentualnej weryfikacji).
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

        // Weryfikacja (wymuszona przy przenoszeniu — nie kasujemy źródła bez pewnej kopii).
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

        // Przy przenoszeniu skasuj źródło — dopiero gdy kopia jest pewna.
        if (options.Mode == ImportMode.Move)
            TryDelete(source);

        return new ImportItemResult(source, ImportOutcome.Imported, targetPath);
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
