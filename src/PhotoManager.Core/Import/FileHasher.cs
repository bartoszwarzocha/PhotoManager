using System.Security.Cryptography;

namespace PhotoManager.Core.Import;

/// <summary>Liczy skrót zawartości pliku — używany do pewnej deduplikacji i weryfikacji kopii.</summary>
public static class FileHasher
{
    /// <summary>Skrót SHA-256 pliku jako hex (małe litery). Strumieniowo, bez ładowania całości do pamięci.</summary>
    public static async Task<string> ComputeAsync(string filePath, CancellationToken ct = default)
    {
        await using var stream = new FileStream(
            filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 1 << 20, useAsync: true);

        using var sha = SHA256.Create();
        var hash = await sha.ComputeHashAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Kopiuje plik i JEDNOCZEŚNIE liczy jego skrót — jeden odczyt źródła zamiast dwóch
    /// (kopiowanie + osobne hashowanie). Zwraca skrót SHA-256 skopiowanej zawartości.
    /// </summary>
    public static async Task<string> CopyAndHashAsync(string source, string dest, CancellationToken ct = default)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1 << 20];

        await using var src = new FileStream(
            source, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, useAsync: true);
        await using var dst = new FileStream(
            dest, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, useAsync: true);

        int read;
        while ((read = await src.ReadAsync(buffer, ct)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, read), ct);
            sha.AppendData(buffer, 0, read);
        }
        return Convert.ToHexStringLower(sha.GetHashAndReset());
    }
}
