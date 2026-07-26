using System.Drawing;
using System.Windows;
using PhotoManager.Core.Config;
using PhotoManager.Core.Devices;
using WinForms = System.Windows.Forms;

namespace PhotoManager.App;

public partial class App : System.Windows.Application
{
    private WinForms.NotifyIcon? _tray;
    private DeviceMonitor? _monitor;
    private DeviceNotificationWindow? _devWindow;
    private AppConfig _config = new();

    // Jedno okno podglądu obsługujące wszystkie podłączone nośniki (lista źródeł w środku).
    private ImportPreviewWindow? _preview;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _config = AppConfig.Load();

        SetupTray();

        _devWindow = new DeviceNotificationWindow();
        _devWindow.DeviceChanged += () =>
        {
            // Skan poza wątkiem UI — enumeracja dysków/MTP potrafi chwilę potrwać.
            var m = _monitor;
            if (m is not null) Task.Run(() => m.ScanNow());
        };

        _monitor = new DeviceMonitor();
        _monitor.DeviceConnected += OnDeviceConnected;
        _monitor.DeviceDisconnected += OnDeviceDisconnected;
        _monitor.Start();

        // Pierwszy start bez skonfigurowanego folderu docelowego: najpierw poproś o ustawienia,
        // dopiero potem ma sens zachęcać do podłączania aparatu.
        if (string.IsNullOrWhiteSpace(_config.DefaultDestination) && _config.Devices.Count == 0)
        {
            _tray!.ShowBalloonTip(5000, "PhotoManager — konfiguracja",
                "Ustaw folder docelowy, aby zacząć.", WinForms.ToolTipIcon.Info);
            Dispatcher.BeginInvoke(OpenSettings);
        }
        else
        {
            _tray!.ShowBalloonTip(3000, "PhotoManager",
                "Działa w tle. Podłącz aparat lub kartę.", WinForms.ToolTipIcon.Info);
        }
    }

    private void SetupTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Importuj ręcznie…", null, (_, _) => OpenManualImport());
        menu.Items.Add("Przenieś bibliotekę…", null, (_, _) => OpenMoveLibrary());
        menu.Items.Add("Ustawienia…", null, (_, _) => OpenSettings());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Zakończ", null, (_, _) => ExitApp());

        _tray = new WinForms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Visible = true,
            Text = "PhotoManager",
            ContextMenuStrip = menu,
        };
        _tray.DoubleClick += (_, _) => OpenManualImport();
    }

    private static Icon LoadTrayIcon()
    {
        try
        {
            var stream = typeof(App).Assembly.GetManifestResourceStream("PhotoManager.App.appicon.ico");
            if (stream is not null) return new Icon(stream);
        }
        catch { /* awaryjnie ikona systemowa */ }
        return SystemIcons.Application;
    }

    private void OnDeviceConnected(object? sender, DeviceInfo device)
    {
        // Reagujemy tylko na nośniki, z których da się czytać zdjęcia z systemu plików:
        // karta/aparat w trybie dysku z folderem DCIM. (MTP wymaga osobnej ścieżki — później.)
        if (device.Kind != DeviceKind.MassStorage || !device.HasDcim || device.PhotoRoot is null)
            return;

        Dispatcher.Invoke(() =>
        {
            switch (_config.OnDetect)
            {
                case OnDetectAction.NotifyOnly:
                    _tray?.ShowBalloonTip(4000, "Wykryto nośnik zdjęć",
                        $"{device.Name} — kliknij ikonę, aby zaimportować.", WinForms.ToolTipIcon.Info);
                    break;

                case OnDetectAction.AutoImport when HasDestination(device) && DestinationAvailable(device):
                    _tray?.ShowBalloonTip(4000, "Wykryto nośnik zdjęć",
                        $"{device.Name} — importuję automatycznie…", WinForms.ToolTipIcon.Info);
                    _ = AutoImportAsync(device);
                    break;

                default: // ShowPreview (lub AutoImport bez skonfigurowanego folderu)
                    _tray?.ShowBalloonTip(4000, "Wykryto nośnik zdjęć",
                        $"{device.Name} — otwieram podgląd importu…", WinForms.ToolTipIcon.Info);
                    ShowPreviewFor(device);
                    break;
            }
        });
    }

    private void OnDeviceDisconnected(object? sender, DeviceInfo device)
    {
        Dispatcher.Invoke(() => _preview?.RemoveSource(device.Id));
    }

    private bool HasDestination(DeviceInfo device)
        => _config.BuildOptions(device.Id, _config.DefaultMode).DestinationRoot is { Length: > 0 };

    private bool DestinationAvailable(DeviceInfo device)
        => Volumes.DriveAvailable(_config.ResolvedDestination(device.Id));

    private async Task AutoImportAsync(DeviceInfo device)
    {
        var source = device.PhotoRoot ?? device.RootPath;
        if (source is null) return;

        // Użyj rozwiązanej ścieżki (dysk przenośny mógł dostać inną literę).
        var resolved = _config.ResolvedDestination(device.Id);
        var options = _config.BuildOptions(device.Id, _config.DefaultMode) with { DestinationRoot = resolved };
        try
        {
            var importer = new Core.Import.Importer();
            var report = await importer.ImportAsync(source, options);
            _tray?.ShowBalloonTip(5000, "Import zakończony",
                $"{device.Name}: nowych {report.Imported}, duplikatów {report.Duplicates}, błędów {report.Failed}.",
                WinForms.ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            _tray?.ShowBalloonTip(6000, "Import nieudany", ex.Message, WinForms.ToolTipIcon.Error);
        }
    }

    private ImportPreviewWindow EnsurePreview()
    {
        if (_preview is null)
        {
            _preview = new ImportPreviewWindow(_config);
            _preview.Closed += (_, _) =>
            {
                _preview = null;
                _config.Save(); // profile urządzeń mogły się zmienić (np. folder docelowy)
            };
            _preview.Show();
        }
        return _preview;
    }

    private void ShowPreviewFor(DeviceInfo device)
    {
        var window = EnsurePreview();
        window.AddSource(device);   // dodaje/odświeża źródło; kilka kart w jednym oknie
        window.Activate();
    }

    private void OpenManualImport()
    {
        // Ręczny import: wskaż folder źródłowy (np. E:\DCIM) i użyj tego samego okna podglądu.
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Wskaż folder ze zdjęciami (np. E:\\DCIM)" };
        if (dlg.ShowDialog() != true) return;

        var name = System.IO.Path.GetFileName(dlg.FolderName.TrimEnd('\\', '/'));
        var device = new DeviceInfo
        {
            Id = "manual:" + dlg.FolderName,
            Name = string.IsNullOrEmpty(name) ? "Ręczny wybór" : $"Ręczny: {name}",
            Kind = DeviceKind.MassStorage,
            RootPath = dlg.FolderName,
            PhotoRoot = dlg.FolderName,
            HasDcim = true,
        };
        ShowPreviewFor(device);
    }

    private void OpenSettings()
    {
        var win = new SettingsWindow(_config);
        if (win.ShowDialog() == true)
            _config.Save();
    }

    private void OpenMoveLibrary()
    {
        // Okno samo zapisuje konfigurację po udanym przeniesieniu (przekierowanie ścieżek).
        new MoveLibraryWindow(_config).ShowDialog();
    }

    private void ExitApp()
    {
        _monitor?.DisposeAsync().AsTask().Wait(1000);
        _devWindow?.Dispose();
        if (_tray is not null) { _tray.Visible = false; _tray.Dispose(); }
        Shutdown();
    }
}
