using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhotoManager.Core.Import;

/// <summary>Wynik szybkiej (bez hashowania) oceny, czy plik ze źródła jest już w bibliotece.</summary>
public enum DedupDecision
{
    /// <summary>Brak pliku o tym rozmiarze w bibliotece — na pewno nowy (bez czytania zawartości).</summary>
    New,

    /// <summary>Jest wpis o tym samym rozmiarze i nazwie — praktycznie na pewno duplikat.</summary>
    Duplicate,

    /// <summary>Jest wpis o tym samym rozmiarze, ale inną nazwą — trzeba potwierdzić skrótem.</summary>
    NeedsHash,
}

/// <summary>
/// Trwały rejestr już zaimportowanych plików. Klucz główny to skrót SHA-256 zawartości, ale
/// dla szybkości utrzymywany jest też indeks po rozmiarze — dzięki czemu w typowym przypadku
/// (import tej samej karty) rozpoznanie duplikatów NIE wymaga czytania plików.
/// Zapisywany w <c>&lt;Cel&gt;\.photomanager\manifest.json</c>.
/// </summary>
public sealed class ImportManifest
{
    private readonly string _path;
    private readonly Dictionary<string, ManifestEntry> _byHash;
    // Indeks po rozmiarze — buduje się z _byHash, nie jest serializowany.
    private readonly Dictionary<long, List<ManifestEntry>> _bySize = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private ImportManifest(string path, Dictionary<string, ManifestEntry> byHash)
    {
        _path = path;
        _byHash = byHash;
        foreach (var e in byHash.Values) IndexBySize(e);
    }

    /// <summary>Standardowa lokalizacja manifestu dla danego folderu docelowego.</summary>
    public static string PathFor(string destinationRoot)
        => Path.Combine(destinationRoot, ".photomanager", "manifest.json");

    public static ImportManifest Load(string destinationRoot)
    {
        var path = PathFor(destinationRoot);
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var data = JsonSerializer.Deserialize<Dictionary<string, ManifestEntry>>(json, JsonOpts);
                if (data is not null)
                    return new ImportManifest(path, data);
            }
        }
        catch
        {
            // Uszkodzony/nieczytelny manifest nie może blokować importu — startujemy z pustym.
        }
        return new ImportManifest(path, new Dictionary<string, ManifestEntry>());
    }

    /// <summary>
    /// Szybka ocena bez czytania pliku: na podstawie rozmiaru i nazwy. Hash potrzebny tylko,
    /// gdy zwróci <see cref="DedupDecision.NeedsHash"/> (rzadka kolizja rozmiaru z inną nazwą).
    /// </summary>
    public DedupDecision FastCheck(string fileName, long size)
    {
        if (!_bySize.TryGetValue(size, out var list))
            return DedupDecision.New;

        foreach (var e in list)
            if (string.Equals(Path.GetFileName(e.RelativePath), fileName, StringComparison.OrdinalIgnoreCase))
                return DedupDecision.Duplicate;

        return DedupDecision.NeedsHash;
    }

    public bool Contains(string hash) => _byHash.ContainsKey(hash);

    public bool TryGet(string hash, out ManifestEntry entry) => _byHash.TryGetValue(hash, out entry!);

    public void Add(string hash, ManifestEntry entry)
    {
        _byHash[hash] = entry;
        IndexBySize(entry);
    }

    private void IndexBySize(ManifestEntry entry)
    {
        if (!_bySize.TryGetValue(entry.Size, out var list))
            _bySize[entry.Size] = list = new List<ManifestEntry>();
        list.Add(entry);
    }

    public int Count => _byHash.Count;

    public void Save()
    {
        var dir = Path.GetDirectoryName(_path)!;
        System.IO.Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(_byHash, JsonOpts);
        // Zapis atomowy: najpierw plik tymczasowy, potem podmiana — chroni przed uszkodzeniem przy przerwaniu.
        var tmp = _path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, _path, overwrite: true);
    }
}

public readonly record struct ManifestEntry(
    [property: JsonPropertyName("path")] string RelativePath,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("date")] DateTime CaptureDate);
