namespace PhotoManager.Core.Import;

public enum ImportOutcome
{
    /// <summary>Skopiowany (lub przeniesiony) do celu.</summary>
    Imported,

    /// <summary>Pominięty — identyczna zawartość już istnieje w bibliotece (deduplikacja).</summary>
    SkippedDuplicate,

    /// <summary>Błąd — plik nie został zaimportowany.</summary>
    Failed,
}

/// <summary>Wynik przetworzenia pojedynczego pliku.</summary>
public sealed record ImportItemResult(
    string SourcePath,
    ImportOutcome Outcome,
    string? TargetPath = null,
    string? Message = null);

/// <summary>Podsumowanie całego importu.</summary>
public sealed class ImportReport
{
    public List<ImportItemResult> Items { get; } = new();

    public int Imported => Items.Count(i => i.Outcome == ImportOutcome.Imported);
    public int Duplicates => Items.Count(i => i.Outcome == ImportOutcome.SkippedDuplicate);
    public int Failed => Items.Count(i => i.Outcome == ImportOutcome.Failed);
    public int Total => Items.Count;

    public long BytesImported { get; set; }
}

/// <summary>Zdarzenie postępu zgłaszane w trakcie importu (do pokazania paska/logu).</summary>
public readonly record struct ImportProgress(
    int Current,
    int Total,
    string CurrentFile,
    ImportOutcome? LastOutcome);
