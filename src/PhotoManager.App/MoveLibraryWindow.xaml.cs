using System.IO;
using System.Windows;
using PhotoManager.Core.Config;
using PhotoManager.Core.Devices;
using PhotoManager.Core.Import;
using MessageBox = System.Windows.MessageBox;
using L = PhotoManager.App.Localization.Loc;

namespace PhotoManager.App;

public partial class MoveLibraryWindow : Window
{
    private readonly AppConfig _config;
    private CancellationTokenSource? _cts;
    private bool _busy;

    public MoveLibraryWindow(AppConfig config)
    {
        InitializeComponent();
        Icon = AppIcons.Window;
        _config = config;

        // Domyślnie źródło = aktualna domyślna biblioteka (rozwiązana po serialu).
        var source = string.IsNullOrEmpty(config.DefaultDestination)
            ? "" : (Volumes.Resolve(config.DefaultDestination, config.DefaultDestinationSerial) ?? config.DefaultDestination);
        SourceBox.Text = source;

        Closing += (_, _) => _cts?.Cancel();
    }

    private void ChangeSource_Click(object sender, RoutedEventArgs e) => Pick(SourceBox, L.Get("Move_PickSource"));
    private void ChangeDest_Click(object sender, RoutedEventArgs e) => Pick(DestBox, L.Get("Move_PickDest"));

    private void Pick(System.Windows.Controls.TextBox box, string title)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = title };
        if (!string.IsNullOrEmpty(box.Text) && Directory.Exists(box.Text))
            dlg.InitialDirectory = box.Text;
        if (dlg.ShowDialog() == true)
            box.Text = dlg.FolderName;
    }

    private async void Move_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var source = SourceBox.Text.Trim();
        var dest = DestBox.Text.Trim();

        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(dest))
        {
            MessageBox.Show(this, L.Get("Move_PickBoth"), "PhotoManager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!Directory.Exists(source))
        {
            MessageBox.Show(this, L.Get("Move_SourceMissing"), "PhotoManager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!Volumes.DriveAvailable(dest))
        {
            MessageBox.Show(this, L.Get("Move_DestUnavailable", Volumes.DriveRoot(dest)), "PhotoManager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(this,
            L.Get("Move_Confirm", source, dest),
            L.Get("Move_ConfirmTitle"), MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.Yes) return;

        _cts = new CancellationTokenSource();
        SetBusy(true);
        StatusText.Text = L.Get("Move_Working");

        var progress = new Progress<MoveProgress>(p =>
        {
            Progress.Value = p.Total == 0 ? 0 : 100.0 * p.Current / p.Total;
            StatusText.Text = $"{p.Current}/{p.Total} — {Path.GetFileName(p.CurrentFile)}";
        });

        MoveReport? report = null;
        bool cancelled = false;
        try
        {
            var mover = new LibraryMover();
            report = await mover.MoveAsync(source, dest, progress, _cts.Token);
        }
        catch (OperationCanceledException) { cancelled = true; }
        catch (Exception ex)
        {
            SetBusy(false);
            MessageBox.Show(this, L.Get("Move_Failed", ex.Message), "PhotoManager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetBusy(false);

        if (cancelled || report is null)
        {
            Progress.Value = 0;
            StatusText.Text = L.Get("Move_Cancelled");
            return;
        }

        // Zaktualizuj konfigurację: ścieżki wskazujące na starą bibliotekę → nowa.
        if (report.SourceRemoved)
        {
            RepointConfig(source, dest);
            _config.Save();
        }

        Progress.Value = 100;
        if (report.FastMoved)
            StatusText.Text = L.Get("Move_DoneFast");
        else
            StatusText.Text = L.Get("Move_DoneStatus", report.Copied, report.Failed,
                report.SourceRemoved ? L.Get("Move_SrcRemoved") : L.Get("Move_SrcKept"));

        var msg = report.FastMoved
            ? L.Get("Move_DoneMsgFast")
            : L.Get("Move_DoneMsg", report.Copied, report.Failed, report.Bytes / 1_048_576.0,
                report.SourceRemoved ? L.Get("Move_SrcRemovedFull") : L.Get("Move_SrcKeptFull"));
        MessageBox.Show(this, msg, L.Get("Move_DoneTitle"),
            MessageBoxButton.OK, report.Failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    /// <summary>Przekierowuje ścieżki w konfiguracji ze starej lokalizacji na nową.</summary>
    private void RepointConfig(string oldRoot, string newRoot)
    {
        if (PathsEqual(_config.DefaultDestination, oldRoot))
        {
            _config.DefaultDestination = newRoot;
            _config.DefaultDestinationSerial = Volumes.GetSerial(newRoot);
        }
        foreach (var p in _config.Devices.Values)
        {
            if (p.Destination is { Length: > 0 } d && PathsEqual(d, oldRoot))
            {
                p.Destination = newRoot;
                p.DestinationSerial = Volumes.GetSerial(newRoot);
            }
        }
    }

    private static bool PathsEqual(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CancelButton.IsEnabled = busy;
        MoveButton.IsEnabled = !busy;
        CloseButton.IsEnabled = !busy;
    }
}
