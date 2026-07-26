using System.Text;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;
using Directory = MetadataExtractor.Directory;

namespace PhotoManager.Core.Metadata;

/// <summary>Odczyt daty wykonania zdjęcia. Preferuje EXIF DateTimeOriginal, z fallbackiem na daty pliku.</summary>
public static class PhotoMetadata
{
    /// <summary>
    /// Zwraca datę wykonania zdjęcia. Kolejność: EXIF „DateTimeOriginal” → EXIF „DateTime”
    /// → wcześniejsza z dat pliku (utworzenia/modyfikacji). Nigdy nie rzuca — w razie problemu
    /// używa daty pliku, żeby import zawsze mógł ustalić folder docelowy.
    /// </summary>
    public static DateTime GetCaptureDate(string filePath)
    {
        try
        {
            IReadOnlyList<Directory> dirs = ImageMetadataReader.ReadMetadata(filePath);

            var subIfd = dirs.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd is not null &&
                subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var original))
                return original;

            var ifd0 = dirs.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 is not null &&
                ifd0.TryGetDateTime(ExifDirectoryBase.TagDateTime, out var dt))
                return dt;
        }
        catch
        {
            // Nieobsługiwany format / brak EXIF (np. część RAW, filmy) — schodzimy do dat pliku.
        }

        return FileDateFallback(filePath);
    }

    private static DateTime FileDateFallback(string filePath)
    {
        try
        {
            var created = File.GetCreationTime(filePath);
            var modified = File.GetLastWriteTime(filePath);
            // Wcześniejsza data jest zwykle bliższa faktycznemu wykonaniu (kopiowanie odświeża „utworzono”).
            return created < modified ? created : modified;
        }
        catch
        {
            return DateTime.Now;
        }
    }

    /// <summary>
    /// Zwraca szczegóły z metadanych zdjęcia do panelu podglądu. <c>Label</c> to stabilny token
    /// (np. „Camera", „Lens") — niezależny od języka; aplikacja tłumaczy go na etykietę.
    /// Puste wartości pomija; nigdy nie rzuca.
    /// </summary>
    public static IReadOnlyList<(string Label, string Value)> GetDetails(string filePath)
    {
        var items = new List<(string, string)>();
        try
        {
            IReadOnlyList<Directory> dirs = ImageMetadataReader.ReadMetadata(filePath);

            // Skanujemy WSZYSTKIE katalogi po numerze tagu — dla RAW (ARW) tagi EXIF bywają
            // w innym katalogu niż standardowy ExifSubIfd, więc sam OfType by je pominął.
            var camera = string.Join(" ", new[]
            {
                Find(dirs, ExifDirectoryBase.TagMake),
                Find(dirs, ExifDirectoryBase.TagModel),
            }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();
            Add(items, "Camera", camera);

            Add(items, "Lens", Find(dirs, ExifDirectoryBase.TagLensModel));
            Add(items, "FocalLength", Find(dirs, ExifDirectoryBase.TagFocalLength));
            Add(items, "Aperture", Find(dirs, ExifDirectoryBase.TagFNumber));
            Add(items, "Shutter", Find(dirs, ExifDirectoryBase.TagExposureTime));
            Add(items, "Iso", Find(dirs, ExifDirectoryBase.TagIsoEquivalent));
            Add(items, "Mode", Find(dirs, ExifDirectoryBase.TagExposureProgram));
            Add(items, "Metering", Find(dirs, ExifDirectoryBase.TagMeteringMode));
            Add(items, "WhiteBalance", Find(dirs, ExifDirectoryBase.TagWhiteBalance));
            Add(items, "Flash", Find(dirs, ExifDirectoryBase.TagFlash));

            // Pola specyficzne dla Sony (z MakerNotes) — tylko z katalogu Sony, by uniknąć kolizji tagów.
            Add(items, "Focus", FindInType<SonyType1MakernoteDirectory>(dirs, 0x201B));         // Focus Mode
            Add(items, "Stabilization", FindInType<SonyType1MakernoteDirectory>(dirs, 0xB026)); // Image Stabilisation
            Add(items, "Dro", FindInType<SonyType1MakernoteDirectory>(dirs, 0xB025));           // Dynamic Range Optimizer

            // Wymiary wyjściowe (jak w JPG) oraz — dla RAW — natywna klatka matrycy.
            int? w = FindInt(dirs, ExifDirectoryBase.TagExifImageWidth);
            int? h = FindInt(dirs, ExifDirectoryBase.TagExifImageHeight);
            if (w is int ww && h is int hh)
                Add(items, "Dimensions", $"{ww} × {hh}");

            int? rw = FindInt(dirs, ExifDirectoryBase.TagImageWidth);
            int? rh = FindInt(dirs, ExifDirectoryBase.TagImageHeight);
            if (rw is int rww && rh is int rhh && (rww != w || rhh != h))
                Add(items, "Sensor", $"{rww} × {rhh}");

            if (FindDate(dirs, ExifDirectoryBase.TagDateTimeOriginal) is { } dt)
                Add(items, "Taken", dt.ToString("yyyy-MM-dd HH:mm:ss"));
        }
        catch
        {
            // Brak/niepełne EXIF (część RAW, filmy) — zwracamy, co się udało.
        }
        return items;
    }

    /// <summary>Pierwszy niepusty opis danego tagu spośród wszystkich katalogów metadanych.</summary>
    private static string? Find(IReadOnlyList<Directory> dirs, int tag)
    {
        foreach (var d in dirs)
            if (d.ContainsTag(tag))
            {
                var s = d.GetDescription(tag);
                if (!string.IsNullOrWhiteSpace(s)) return s;
            }
        return null;
    }

    private static DateTime? FindDate(IReadOnlyList<Directory> dirs, int tag)
    {
        foreach (var d in dirs)
            if (d.ContainsTag(tag) && d.TryGetDateTime(tag, out var dt))
                return dt;
        return null;
    }

    private static int? FindInt(IReadOnlyList<Directory> dirs, int tag)
    {
        foreach (var d in dirs)
            if (d.ContainsTag(tag) && d.TryGetInt32(tag, out var v))
                return v;
        return null;
    }

    /// <summary>Opis tagu wyłącznie z katalogu wskazanego typu (np. Sony MakerNotes) — unika kolizji numerów tagów.</summary>
    private static string? FindInType<T>(IReadOnlyList<Directory> dirs, int tag) where T : Directory
    {
        var d = dirs.OfType<T>().FirstOrDefault(x => x.ContainsTag(tag));
        var s = d?.GetDescription(tag);
        return string.IsNullOrWhiteSpace(s) ? null : s;
    }

    private static void Add(List<(string, string)> items, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            items.Add((label, value.Trim()));
    }

    /// <summary>Diagnostyka: wypisuje wszystkie katalogi i tagi z pliku (wraz z nazwą katalogu i numerem tagu).</summary>
    public static string DumpAll(string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Plik: {filePath}");
        try
        {
            var dirs = ImageMetadataReader.ReadMetadata(filePath);
            foreach (var d in dirs)
            {
                sb.AppendLine($"== {d.Name} ({d.TagCount} tagów) ==");
                foreach (var t in d.Tags)
                    sb.AppendLine($"   [0x{t.Type:X4}] {t.Name} = {t.Description}");
                if (d.HasError)
                    foreach (var e in d.Errors)
                        sb.AppendLine($"   ! {e}");
            }
        }
        catch (Exception ex)
        {
            sb.AppendLine($"BŁĄD odczytu: {ex.Message}");
        }
        return sb.ToString();
    }
}
