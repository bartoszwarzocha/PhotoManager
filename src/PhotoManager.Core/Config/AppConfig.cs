using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoManager.Core.Import;

namespace PhotoManager.Core.Config;

/// <summary>
/// Ustawienia aplikacji zapisywane w <c>%APPDATA%\PhotoManager\config.json</c>.
/// Trzyma domyślne parametry importu oraz profile per urządzenie (dopasowywane po Id z monitora).
/// </summary>
public sealed class AppConfig
{
    /// <summary>Domyślny folder biblioteki, gdy urządzenie nie ma własnego profilu.</summary>
    public string DefaultDestination { get; set; } = "";

    /// <summary>Serial woluminu domyślnej biblioteki — pozwala odnaleźć ją po zmianie litery dysku.</summary>
    public string? DefaultDestinationSerial { get; set; }

    /// <summary>Wzorzec podfolderów budowany z daty EXIF (jak w <see cref="ImportOptions.FolderPattern"/>).</summary>
    public string FolderPattern { get; set; } = "yyyy/yyyy-MM-dd";

    /// <summary>Po skopiowaniu weryfikować kopię skrótem.</summary>
    public bool VerifyAfterCopy { get; set; } = true;

    /// <summary>Uruchamiać się z Windows (obsłuż w aplikacji przez wpis w rejestrze/Autostart).</summary>
    public bool RunAtStartup { get; set; } = false;

    /// <summary>Domyślny tryb importu (Kopiuj/Przenieś) proponowany w oknie podglądu.</summary>
    public ImportMode DefaultMode { get; set; } = ImportMode.Copy;

    /// <summary>Co zrobić po wykryciu karty aparatu.</summary>
    public OnDetectAction OnDetect { get; set; } = OnDetectAction.ShowPreview;

    /// <summary>Obsługiwane rozszerzenia (z kropką, małe litery). Puste = zestaw domyślny.</summary>
    public List<string> Extensions { get; set; } = ImportOptions.DefaultExtensions.OrderBy(e => e).ToList();

    /// <summary>Profile per urządzenie, klucz = <c>DeviceInfo.Id</c>.</summary>
    public Dictionary<string, DeviceProfile> Devices { get; set; } = new();

    /// <summary>Skonfigurowany folder docelowy dla urządzenia (profil > domyślny), bez rozwiązywania.</summary>
    public (string Path, string? Serial) DestinationFor(string deviceId)
    {
        Devices.TryGetValue(deviceId, out var profile);
        if (profile?.Destination is { Length: > 0 } d)
            return (d, profile.DestinationSerial);
        return (DefaultDestination, DefaultDestinationSerial);
    }

    /// <summary>
    /// Aktualna ścieżka biblioteki dla urządzenia — z próbą odnalezienia po serialu, gdy zmieniła
    /// się litera dysku. Zwraca skonfigurowaną ścieżkę, jeśli nośnika nie da się odnaleźć.
    /// </summary>
    public string ResolvedDestination(string deviceId)
    {
        var (path, serial) = DestinationFor(deviceId);
        return PhotoManager.Core.Devices.Volumes.Resolve(path, serial) ?? path;
    }

    /// <summary>Zwraca rozszerzenia jako zbiór (z fallbackiem na domyślne, gdy lista pusta).</summary>
    public IReadOnlySet<string> ExtensionSet() =>
        Extensions.Count > 0
            ? new HashSet<string>(Extensions, StringComparer.OrdinalIgnoreCase)
            : ImportOptions.DefaultExtensions;

    // --- Utrwalanie ---

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }, // enumy jako czytelne nazwy w config.json
    };

    public static string DefaultPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PhotoManager", "config.json");

    public static AppConfig Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                if (cfg is not null) return cfg;
            }
        }
        catch
        {
            // Uszkodzona konfiguracja nie może blokować startu — używamy domyślnej.
        }
        return new AppConfig();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(this, JsonOpts);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, json);
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Buduje opcje importu dla danego urządzenia — profil urządzenia ma pierwszeństwo nad domyślnymi.</summary>
    public ImportOptions BuildOptions(string deviceId, ImportMode mode, bool dryRun = false)
    {
        Devices.TryGetValue(deviceId, out var profile);
        return new ImportOptions
        {
            DestinationRoot = profile?.Destination is { Length: > 0 } d ? d : DefaultDestination,
            FolderPattern = profile?.FolderPattern is { Length: > 0 } p ? p : FolderPattern,
            Mode = mode,
            VerifyAfterCopy = VerifyAfterCopy,
            Extensions = ExtensionSet(),
            DryRun = dryRun,
        };
    }
}

/// <summary>Reakcja aplikacji po wykryciu karty aparatu.</summary>
public enum OnDetectAction
{
    /// <summary>Otwórz okno podglądu z listą zdjęć (domyślne).</summary>
    ShowPreview,

    /// <summary>Zaimportuj od razu wg profilu urządzenia, tylko powiadom o wyniku.</summary>
    AutoImport,

    /// <summary>Tylko pokaż dymek, nic nie otwieraj.</summary>
    NotifyOnly,
}

/// <summary>Ustawienia przypięte do konkretnego urządzenia (aparatu/karty), rozpoznawanego po Id.</summary>
public sealed class DeviceProfile
{
    /// <summary>Czytelna nazwa (ostatnio widziana), tylko do wyświetlenia.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Folder docelowy dla tego urządzenia; puste = użyj domyślnego.</summary>
    public string? Destination { get; set; }

    /// <summary>Serial woluminu folderu docelowego — do odnalezienia po zmianie litery dysku.</summary>
    public string? DestinationSerial { get; set; }

    /// <summary>Własny wzorzec folderów; puste = użyj domyślnego.</summary>
    public string? FolderPattern { get; set; }
}
