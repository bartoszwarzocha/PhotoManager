using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PhotoManager.App;

/// <summary>Wspólne źródło ikony aplikacji dla okien WPF (pasek tytułu / pasek zadań).</summary>
public static class AppIcons
{
    public static ImageSource? Window { get; } = Load();

    private static ImageSource? Load()
    {
        try
        {
            using var stream = typeof(AppIcons).Assembly
                .GetManifestResourceStream("PhotoManager.App.appicon.ico");
            if (stream is null) return null;
            var decoder = new IconBitmapDecoder(stream,
                BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            // Największa dostępna klatka wygląda najlepiej na pasku zadań.
            return decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        }
        catch
        {
            return null;
        }
    }
}
