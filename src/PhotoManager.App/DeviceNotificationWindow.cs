using System.Windows.Interop;

namespace PhotoManager.App;

/// <summary>
/// Niewidoczne okno „message-only”, które nasłuchuje WM_DEVICECHANGE. Dzięki temu aplikacja
/// reaguje na podłączenie/odłączenie sprzętu natychmiast, zamiast czekać na kolejny cykl skanu.
/// Monitor i tak skanuje cyklicznie — to tylko przyspieszenie reakcji.
/// </summary>
public sealed class DeviceNotificationWindow : IDisposable
{
    private const int WM_DEVICECHANGE = 0x0219;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly HwndSource _source;

    /// <summary>Zgłaszane (na wątku UI) przy każdej zmianie urządzeń.</summary>
    public event Action? DeviceChanged;

    public DeviceNotificationWindow()
    {
        var parameters = new HwndSourceParameters("PhotoManagerDeviceNotify")
        {
            ParentWindow = HWND_MESSAGE, // okno tylko do komunikatów, bez UI
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_DEVICECHANGE)
            DeviceChanged?.Invoke();
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
