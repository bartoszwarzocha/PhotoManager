using System.ComponentModel;
using System.IO;
using PhotoManager.App.Localization;

namespace PhotoManager.App;

/// <summary>Logiczny stan wiersza (niezależny od języka).</summary>
public enum RowStatus { Pending, Unknown, New, Duplicate, Copied, Moved, Error }

/// <summary>Jeden plik na liście podglądu importu (z zaznaczeniem i statusem analizy).</summary>
public sealed class FileRow : INotifyPropertyChanged
{
    public string Path { get; }
    public string FileName { get; }
    public long SizeBytes { get; }

    // Data początkowo z metadanych pliku (natychmiast), potem uściślana z EXIF w tle.
    private DateTime _date;
    public DateTime Date
    {
        get => _date;
        set { if (_date != value) { _date = value; OnChanged(nameof(Date)); OnChanged(nameof(DateDisplay)); } }
    }

    private bool _selected = true;
    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; OnChanged(nameof(Selected)); } }
    }

    private RowStatus _state = RowStatus.Pending;
    public RowStatus State
    {
        get => _state;
        set { if (_state != value) { _state = value; OnChanged(nameof(State)); OnChanged(nameof(Status)); } }
    }

    /// <summary>Tekst statusu w bieżącym języku (do wyświetlenia w tabeli).</summary>
    public string Status => _state switch
    {
        RowStatus.New => Loc.Get("Status_New"),
        RowStatus.Duplicate => Loc.Get("Status_Duplicate"),
        RowStatus.Copied => Loc.Get("Status_Copied"),
        RowStatus.Moved => Loc.Get("Status_Moved"),
        RowStatus.Error => Loc.Get("Status_Error"),
        RowStatus.Unknown => Loc.Get("Status_Unknown"),
        _ => Loc.Get("Status_Pending"),
    };

    public string DateDisplay => Date == default ? "" : Date.ToString("yyyy-MM-dd HH:mm");
    public string SizeDisplay => FormatSize(SizeBytes);

    public FileRow(string path)
    {
        Path = path;
        FileName = System.IO.Path.GetFileName(path);
        // Tylko szybkie metadane systemu plików — bez otwierania/parsowania EXIF.
        try
        {
            var fi = new FileInfo(path);
            SizeBytes = fi.Length;
            _date = fi.LastWriteTime; // tymczasowo; zostanie zastąpione datą EXIF
        }
        catch
        {
            SizeBytes = 0;
        }
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v:0.#} {units[u]}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
