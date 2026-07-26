namespace PhotoManager.Core.Import;

public enum ImportMode
{
    /// <summary>Kopiuj — pliki źródłowe zostają nienaruszone.</summary>
    Copy,

    /// <summary>Przenieś — plik źródłowy kasowany dopiero po zweryfikowanej kopii.</summary>
    Move,
}

/// <summary>Ustawienia jednej operacji importu. W M3 będą wczytywane z config.json.</summary>
public sealed record ImportOptions
{
    public required string DestinationRoot { get; init; }

    public ImportMode Mode { get; init; } = ImportMode.Copy;

    /// <summary>
    /// Wzorzec podfolderów budowany z daty wykonania przez <see cref="DateTime.ToString(string)"/>.
    /// Ukośnik „/” oznacza zagnieżdżenie. Domyślnie: RRRR/RRRR-MM-DD (np. 2026/2026-07-26).
    /// </summary>
    public string FolderPattern { get; init; } = "yyyy/yyyy-MM-dd";

    /// <summary>Po skopiowaniu policz skrót pliku docelowego i porównaj ze źródłem. Wymagane przy przenoszeniu.</summary>
    public bool VerifyAfterCopy { get; init; } = true;

    /// <summary>Tryb próbny: analizuj i raportuj decyzje (import/duplikat), ale nie kopiuj i nie przenoś.</summary>
    public bool DryRun { get; init; } = false;

    /// <summary>
    /// Przy duplikacie (ta sama nazwa+rozmiar w bibliotece) porównaj dodatkowo ZAWARTOŚĆ skrótem.
    /// Jeśli różna (uszkodzony/inny plik), potraktuj jako nowy i zgraj dobry z karty. Wolniej (hashuje
    /// duplikaty), ale chroni przed uszkodzoną kopią w bibliotece.
    /// </summary>
    public bool VerifyDuplicateContent { get; init; } = false;

    /// <summary>Rozszerzenia traktowane jako zdjęcia/filmy (z kropką, małe litery).</summary>
    public IReadOnlySet<string> Extensions { get; init; } = DefaultExtensions;

    public static readonly IReadOnlySet<string> DefaultExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // RAW
        ".arw", ".cr2", ".cr3", ".nef", ".nrw", ".raf", ".rw2", ".dng", ".orf", ".pef", ".srw",
        // JPEG / HEIF / inne obrazy
        ".jpg", ".jpeg", ".heic", ".heif", ".png", ".tif", ".tiff", ".bmp", ".webp",
        // Filmy
        ".mp4", ".mov", ".avi", ".mts", ".m2ts", ".mxf", ".m4v",
    };
}
