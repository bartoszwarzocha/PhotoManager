using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using PhotoManager.Core.Config;
using PhotoManager.Core.Devices;
using PhotoManager.Core.Import;

namespace PhotoManager.App;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _config;
    private readonly ObservableCollection<DeviceProfileRow> _deviceRows = new();

    public SettingsWindow(AppConfig config)
    {
        InitializeComponent();
        Icon = AppIcons.Window;
        _config = config;

        // --- Ogólne ---
        DestBox.Text = config.DefaultDestination;
        PatternBox.Text = config.FolderPattern;
        CopyRadio.IsChecked = config.DefaultMode == ImportMode.Copy;
        MoveRadio.IsChecked = config.DefaultMode == ImportMode.Move;
        OnDetectCombo.SelectedIndex = config.OnDetect switch
        {
            OnDetectAction.AutoImport => 1,
            OnDetectAction.NotifyOnly => 2,
            _ => 0,
        };
        VerifyCheck.IsChecked = config.VerifyAfterCopy;
        StartupCheck.IsChecked = config.RunAtStartup;
        UpdatePreview();

        // --- Rozszerzenia ---
        ExtBox.Text = string.Join(Environment.NewLine, config.Extensions);

        // --- Urządzenia ---
        foreach (var (id, p) in config.Devices)
            _deviceRows.Add(new DeviceProfileRow
            {
                Id = id,
                DisplayName = p.DisplayName ?? id,
                Destination = p.Destination ?? "",
                FolderPattern = p.FolderPattern ?? "",
                Serial = p.DestinationSerial,
            });
        DevicesGrid.ItemsSource = _deviceRows;
    }

    private void Pattern_Changed(object sender, RoutedEventArgs e) => UpdatePreview();

    private void UpdatePreview()
    {
        if (PreviewText is null) return; // wywołane zanim XAML w pełni się załadował
        var pattern = PatternBox.Text.Trim();
        string sub;
        try
        {
            var rendered = DateTime.Now.ToString(pattern, CultureInfo.InvariantCulture);
            sub = string.Join('\\', rendered.Split('/', StringSplitOptions.RemoveEmptyEntries));
        }
        catch
        {
            PreviewText.Text = "Przykład: (niepoprawny wzorzec)";
            return;
        }
        PreviewText.Text = $"Przykład:  DSC00123.ARW  →  {sub}\\DSC00123.ARW";
    }

    private void ChangeDest_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Domyślny folder biblioteki" };
        if (!string.IsNullOrEmpty(DestBox.Text) && Directory.Exists(DestBox.Text))
            dlg.InitialDirectory = DestBox.Text;
        if (dlg.ShowDialog() == true)
            DestBox.Text = dlg.FolderName;
    }

    private void ResetExt_Click(object sender, RoutedEventArgs e)
        => ExtBox.Text = string.Join(Environment.NewLine, ImportOptions.DefaultExtensions.OrderBy(x => x));

    private void RemoveDevice_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in DevicesGrid.SelectedItems.Cast<DeviceProfileRow>().ToList())
            _deviceRows.Remove(row);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _config.DefaultDestination = DestBox.Text.Trim();
        _config.DefaultDestinationSerial = string.IsNullOrEmpty(_config.DefaultDestination)
            ? null : (Volumes.GetSerial(_config.DefaultDestination) ?? _config.DefaultDestinationSerial);
        _config.FolderPattern = string.IsNullOrWhiteSpace(PatternBox.Text) ? "yyyy/yyyy-MM-dd" : PatternBox.Text.Trim();
        _config.DefaultMode = MoveRadio.IsChecked == true ? ImportMode.Move : ImportMode.Copy;
        _config.OnDetect = OnDetectCombo.SelectedIndex switch
        {
            1 => OnDetectAction.AutoImport,
            2 => OnDetectAction.NotifyOnly,
            _ => OnDetectAction.ShowPreview,
        };
        _config.VerifyAfterCopy = VerifyCheck.IsChecked == true;
        _config.RunAtStartup = StartupCheck.IsChecked == true;
        _config.Extensions = ParseExtensions(ExtBox.Text);

        _config.Devices.Clear();
        foreach (var row in _deviceRows)
        {
            var rowDest = string.IsNullOrWhiteSpace(row.Destination) ? null : row.Destination.Trim();
            _config.Devices[row.Id] = new DeviceProfile
            {
                DisplayName = row.DisplayName,
                Destination = rowDest,
                FolderPattern = string.IsNullOrWhiteSpace(row.FolderPattern) ? null : row.FolderPattern.Trim(),
                // Odśwież serial, jeśli dysk dostępny; inaczej zachowaj zapamiętany.
                DestinationSerial = rowDest is null ? null : (Volumes.GetSerial(rowDest) ?? row.Serial),
            };
        }

        StartupRegistration.Apply(_config.RunAtStartup);

        DialogResult = true;
        Close();
    }

    /// <summary>Parsuje rozszerzenia z pola tekstowego: normalizuje do „.ext” małymi literami, usuwa duplikaty.</summary>
    private static List<string> ParseExtensions(string text)
    {
        var tokens = text.Split(new[] { '\r', '\n', ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var t in tokens)
        {
            var ext = t.Trim().ToLowerInvariant();
            if (!ext.StartsWith('.')) ext = "." + ext;
            if (ext.Length > 1 && seen.Add(ext)) result.Add(ext);
        }
        return result.Count > 0 ? result : ImportOptions.DefaultExtensions.OrderBy(x => x).ToList();
    }
}

/// <summary>Wiersz tabeli profili urządzeń w oknie ustawień.</summary>
public sealed class DeviceProfileRow
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Destination { get; set; } = "";
    public string FolderPattern { get; set; } = "";
    public string? Serial { get; set; }
}
