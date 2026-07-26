using System.Runtime.InteropServices;

namespace PhotoManager.Core.Devices;

/// <summary>
/// Pomocnik woluminów: sprawdza dostępność dysku i pozwala odnaleźć bibliotekę po numerze
/// seryjnym woluminu, nawet gdy zmieniła się litera dysku (typowe dla nośników przenośnych).
/// </summary>
public static class Volumes
{
    /// <summary>Czy dysk, na którym leży ścieżka, jest obecnie dostępny.</summary>
    public static bool DriveAvailable(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            return !string.IsNullOrEmpty(root) && System.IO.Directory.Exists(root);
        }
        catch { return false; }
    }

    /// <summary>Litera/korzeń dysku dla ścieżki (np. „H:\”).</summary>
    public static string DriveRoot(string path)
    {
        try { return Path.GetPathRoot(path) ?? path; }
        catch { return path; }
    }

    /// <summary>Numer seryjny woluminu dla ścieżki (hex, 8 znaków) lub null.</summary>
    public static string? GetSerial(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root)) return null;
            if (GetVolumeInformation(root, null, 0, out uint serial, out _, out _, null, 0))
                return serial.ToString("X8");
        }
        catch { /* brak dostępu */ }
        return null;
    }

    /// <summary>
    /// Zwraca aktualną ścieżkę biblioteki. Jeśli oryginalna istnieje — ją. W przeciwnym razie,
    /// mając zapamiętany serial, szuka tego samego woluminu pod inną literą i przelicza ścieżkę.
    /// Null, gdy nośnika nie ma w systemie.
    /// </summary>
    public static string? Resolve(string originalPath, string? serial)
    {
        try
        {
            if (string.IsNullOrEmpty(originalPath)) return null;
            if (System.IO.Directory.Exists(originalPath)) return originalPath;

            var root = Path.GetPathRoot(originalPath);
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(serial)) return null;
            var rest = originalPath.Length > root.Length ? originalPath[root.Length..] : "";

            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady) continue;
                    if (GetSerial(d.RootDirectory.FullName) == serial)
                        return Path.Combine(d.RootDirectory.FullName, rest);
                }
                catch { /* pomiń niedostępny dysk */ }
            }
        }
        catch { /* nic — zwrócimy null */ }
        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformation(
        string rootPathName,
        System.Text.StringBuilder? volumeNameBuffer, int volumeNameSize,
        out uint volumeSerialNumber,
        out uint maximumComponentLength, out uint fileSystemFlags,
        System.Text.StringBuilder? fileSystemNameBuffer, int fileSystemNameSize);
}
