namespace PhotoManager.Core.Import;

public readonly record struct MoveProgress(int Current, int Total, string CurrentFile);

/// <summary>Podsumowanie przeniesienia biblioteki.</summary>
public sealed class MoveReport
{
    public int Copied;
    public int Failed;
    public long Bytes;
    public bool FastMoved;              // przeniesiono błyskawicznie (ten sam wolumin)
    public bool SourceRemoved;
    public List<string> Errors { get; } = new();
}

/// <summary>
/// Przenosi całą bibliotekę (wszystkie pliki i podfoldery) do nowej lokalizacji —
/// np. na dysk przenośny. Na tym samym woluminie robi to natychmiast; między dyskami kopiuje
/// z weryfikacją skrótu i kasuje źródło dopiero, gdy wszystko się powiedzie.
/// </summary>
public sealed class LibraryMover
{
    public async Task<MoveReport> MoveAsync(
        string sourceRoot, string destRoot,
        IProgress<MoveProgress>? progress = null, CancellationToken ct = default)
    {
        var report = new MoveReport();
        sourceRoot = Path.GetFullPath(sourceRoot);
        destRoot = Path.GetFullPath(destRoot);

        if (!Directory.Exists(sourceRoot))
            throw new DirectoryNotFoundException($"Biblioteka źródłowa nie istnieje: {sourceRoot}");
        if (PathsEqual(sourceRoot, destRoot))
            throw new IOException("Źródło i cel są takie same.");
        if (IsSubPath(destRoot, sourceRoot))
            throw new IOException("Folder docelowy nie może leżeć wewnątrz biblioteki źródłowej.");
        if (IsSubPath(sourceRoot, destRoot))
            throw new IOException("Biblioteka źródłowa nie może leżeć wewnątrz folderu docelowego.");

        // Szybka ścieżka: ten sam wolumin i cel jeszcze nie istnieje → błyskawiczne przeniesienie.
        if (!Directory.Exists(destRoot) && SameVolume(sourceRoot, destRoot))
        {
            try
            {
                var parent = Path.GetDirectoryName(destRoot);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                Directory.Move(sourceRoot, destRoot);
                report.FastMoved = true;
                report.SourceRemoved = true;
                return report;
            }
            catch { /* nie wyszło — kopiujemy plik po pliku */ }
        }

        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories).ToList();
        int index = 0;

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            index++;
            progress?.Report(new MoveProgress(index, files.Count, file));

            try
            {
                var rel = Path.GetRelativePath(sourceRoot, file);
                var target = Path.Combine(destRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);

                var part = target + ".part";
                var srcHash = await FileHasher.CopyAndHashAsync(file, part, ct);
                var tgtHash = await FileHasher.ComputeAsync(part, ct);
                if (srcHash != tgtHash)
                {
                    TryDelete(part);
                    report.Failed++;
                    report.Errors.Add($"weryfikacja nieudana: {rel}");
                    continue;
                }
                File.Move(part, target, overwrite: true);
                report.Copied++;
                try { report.Bytes += new FileInfo(target).Length; } catch { }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                report.Failed++;
                report.Errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        // Kasujemy źródło tylko, gdy WSZYSTKO się udało — inaczej zostaje bezpieczna kopia w obu miejscach.
        if (report.Failed == 0)
        {
            try { Directory.Delete(sourceRoot, recursive: true); report.SourceRemoved = true; }
            catch (Exception ex) { report.Errors.Add($"Nie udało się usunąć źródła: {ex.Message}"); }
        }

        return report;
    }

    private static bool SameVolume(string a, string b)
        => string.Equals(Path.GetPathRoot(a), Path.GetPathRoot(b), StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string a, string b)
        => string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b),
            StringComparison.OrdinalIgnoreCase);

    private static bool IsSubPath(string child, string parent)
    {
        var p = Path.TrimEndingDirectorySeparator(Path.GetFullPath(parent));
        var c = Path.TrimEndingDirectorySeparator(Path.GetFullPath(child));
        return c.StartsWith(p + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
