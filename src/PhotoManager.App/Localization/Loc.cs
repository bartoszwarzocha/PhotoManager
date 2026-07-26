using System.Globalization;
using System.Resources;

namespace PhotoManager.App.Localization;

/// <summary>Dostęp do przetłumaczonych napisów oraz ustawianie języka interfejsu (PL/EN).</summary>
public static class Loc
{
    private static readonly ResourceManager Rm =
        new("PhotoManager.App.Localization.Strings", typeof(Loc).Assembly);

    /// <summary>Aktualna kultura interfejsu (wpływa na wybór zasobów i formatowanie).</summary>
    public static CultureInfo Culture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>Napis dla klucza (fallback: klucz, gdy brak tłumaczenia).</summary>
    public static string Get(string key) => Rm.GetString(key, Culture) ?? key;

    /// <summary>Napis z podstawieniem argumentów ({0}, {1}, …).</summary>
    public static string Get(string key, params object[] args) => string.Format(Culture, Get(key), args);

    /// <summary>Ustawia język: „auto" (wg Windows), „pl" lub „en".</summary>
    public static void Apply(string? language)
    {
        Culture = (language?.ToLowerInvariant()) switch
        {
            "pl" => new CultureInfo("pl"),
            "en" => new CultureInfo("en"),
            _ => CultureInfo.InstalledUICulture,
        };
        CultureInfo.CurrentUICulture = Culture;
        CultureInfo.CurrentCulture = Culture;
        CultureInfo.DefaultThreadCurrentUICulture = Culture;
        CultureInfo.DefaultThreadCurrentCulture = Culture;
    }
}
