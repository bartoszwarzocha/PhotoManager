using MediaDevices;

namespace PhotoManager.Core.Devices;

/// <summary>
/// Wykrywa podłączenie i odłączenie urządzeń ze zdjęciami — zarówno dysków wymiennych
/// (litera + folder DCIM), jak i urządzeń MTP/PTP (aparaty, telefony) przez WPD.
///
/// Działa przez cykliczne skanowanie co <see cref="PollInterval"/>. To celowy wybór:
/// urządzenia MTP nie mają litery dysku i nie wysyłają wygodnych zdarzeń systemu plików,
/// więc jedno spójne podejście (polling) obsługuje oba przypadki. W wersji z GUI komunikat
/// WM_DEVICECHANGE może dodatkowo „szturchnąć” monitor przez <see cref="ScanNow"/>, żeby
/// zareagował natychmiast, bez czekania na kolejny cykl.
///
/// Zdarzenia są zgłaszane z wątku skanującego — konsument w GUI musi je zmarshalować
/// do wątku UI (np. Dispatcher.Invoke).
/// </summary>
public sealed class DeviceMonitor : IAsyncDisposable
{
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(2);

    public event EventHandler<DeviceInfo>? DeviceConnected;
    public event EventHandler<DeviceInfo>? DeviceDisconnected;

    private readonly Dictionary<string, DeviceInfo> _known = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    public IReadOnlyCollection<DeviceInfo> KnownDevices
    {
        get { lock (_sync) return _known.Values.ToList(); }
    }

    public void Start()
    {
        if (_loop is not null) return;
        _cts = new CancellationTokenSource();
        _loop = RunAsync(_cts.Token);
    }

    /// <summary>Wymusza natychmiastowe skanowanie (np. po WM_DEVICECHANGE).</summary>
    public void ScanNow() => Scan();

    private async Task RunAsync(CancellationToken ct)
    {
        // Pierwszy skan od razu — zgłosi już podłączone urządzenia.
        Scan();
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                Scan();
        }
        catch (OperationCanceledException) { /* zamykanie */ }
    }

    private void Scan()
    {
        var current = new Dictionary<string, DeviceInfo>();
        foreach (var d in EnumerateDrives()) current[d.Id] = d;
        foreach (var d in EnumerateMtp()) current[d.Id] = d;

        List<DeviceInfo> added = new();
        List<DeviceInfo> removed = new();

        lock (_sync)
        {
            foreach (var (id, dev) in current)
                if (!_known.ContainsKey(id))
                    added.Add(dev);

            foreach (var (id, dev) in _known)
                if (!current.ContainsKey(id))
                    removed.Add(dev);

            _known.Clear();
            foreach (var (id, dev) in current) _known[id] = dev;
        }

        foreach (var d in added) DeviceConnected?.Invoke(this, d);
        foreach (var d in removed) DeviceDisconnected?.Invoke(this, d);
    }

    private static IEnumerable<DeviceInfo> EnumerateDrives()
    {
        DriveInfo[] drives;
        try { drives = DriveInfo.GetDrives(); }
        catch { yield break; }

        foreach (var drive in drives)
        {
            bool ready;
            try { ready = drive.IsReady && drive.DriveType == DriveType.Removable; }
            catch { ready = false; }
            if (!ready) continue;

            string root = drive.RootDirectory.FullName;
            string? photoRoot = null;
            bool hasDcim = false;
            try
            {
                var dcim = Path.Combine(root, "DCIM");
                hasDcim = Directory.Exists(dcim);
                photoRoot = hasDcim ? dcim : root;
            }
            catch { /* brak dostępu — pomijamy PhotoRoot */ }

            string label;
            try { label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Dysk wymienny" : drive.VolumeLabel; }
            catch { label = "Dysk wymienny"; }

            string serial = Volumes.GetSerial(root) ?? string.Empty;
            string id = string.IsNullOrEmpty(serial) ? $"drive:{root}" : $"vol:{serial}";
            string letter = root.TrimEnd('\\', '/');

            yield return new DeviceInfo
            {
                Id = id,
                Name = $"{label} ({letter})",
                Kind = DeviceKind.MassStorage,
                RootPath = root,
                PhotoRoot = photoRoot,
                HasDcim = hasDcim,
            };
        }
    }

    private static IEnumerable<DeviceInfo> EnumerateMtp()
    {
        IEnumerable<MediaDevice> devices;
        try { devices = MediaDevice.GetDevices(); }
        catch { yield break; }

        foreach (var dev in devices)
        {
            DeviceInfo? info = null;
            try
            {
                // Windows wystawia dyski USB także przez magistralę WPD (wpdbusenum), więc
                // MediaDevices zwraca je jako „urządzenia MTP”. To duplikaty tego, co już
                // mamy jako litery dysków — pomijamy je. Prawdziwy aparat/telefon w trybie
                // MTP/PTP ma DeviceId typu „usb#vid_...”, a nie „usbstor#disk / massstorageclass”.
                string devId = dev.DeviceId?.ToLowerInvariant() ?? string.Empty;
                if (devId.Contains("usbstor") || devId.Contains("massstorage"))
                    continue; // pamięć masowa — już obsłużona jako dysk; finally zwolni dev

                string name = FirstNonEmpty(dev.FriendlyName, dev.Description, dev.Manufacturer) ?? "Urządzenie MTP";
                info = new DeviceInfo
                {
                    Id = $"mtp:{dev.DeviceId}",
                    Name = name,
                    Kind = DeviceKind.Mtp,
                    RootPath = null,
                    PhotoRoot = null, // ustalimy po połączeniu w silniku importu (M2)
                };
            }
            catch { /* nie każde urządzenie WPD da się odpytać bez połączenia */ }
            finally { try { dev.Dispose(); } catch { } }

            if (info is not null) yield return info;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    public async ValueTask DisposeAsync()
    {
        _cts?.Cancel();
        if (_loop is not null)
        {
            try { await _loop; } catch { }
        }
        _cts?.Dispose();
    }
}
