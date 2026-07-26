using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using PhotoManager.Core.Config;
using PhotoManager.Core.Devices;
using PhotoManager.Core.Import;
using PhotoManager.Core.Metadata;
using MessageBox = System.Windows.MessageBox;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBlock = System.Windows.Controls.TextBlock;
using L = PhotoManager.App.Localization.Loc;

namespace PhotoManager.App;

public partial class ImportPreviewWindow : Window
{
    private DeviceInfo? _device;
    private readonly AppConfig _config;
    private string _source = "";
    private readonly ObservableCollection<FileRow> _rows = new();
    private readonly ObservableCollection<DeviceInfo> _sources = new();
    private bool _busy;
    private CancellationTokenSource? _cts;
    private readonly HashSet<string> _enabledExts = new(StringComparer.OrdinalIgnoreCase);
    private ICollectionView? _view;

    public ImportPreviewWindow(AppConfig config)
    {
        InitializeComponent();
        Icon = AppIcons.Window;
        _config = config;

        // Domyślny tryb z ustawień.
        CopyRadio.IsChecked = _config.DefaultMode == ImportMode.Copy;
        MoveRadio.IsChecked = _config.DefaultMode == ImportMode.Move;

        FilesGrid.ItemsSource = _rows;
        SourceCombo.ItemsSource = _sources;
        Closing += (_, _) => _cts?.Cancel();
        ShowEmptyState();
    }

    // --- Zarządzanie źródłami (kilka kart naraz) ---

    /// <summary>Dodaje (lub odświeża) podłączony nośnik jako źródło. Pierwszy staje się wybrany.</summary>
    public void AddSource(DeviceInfo device)
    {
        var idx = IndexOfSource(device.Id);
        if (idx >= 0)
        {
            _sources[idx] = device; // litera/nazwa mogła się zmienić
            if (SourceCombo.SelectedIndex == idx) _ = LoadSelectedSourceAsync();
        }
        else
        {
            _sources.Add(device);
            if (SourceCombo.SelectedItem is null) SourceCombo.SelectedIndex = 0;
        }
        Activate();
    }

    /// <summary>Usuwa odłączony nośnik. Jeśli był wybrany — przełącza na inny albo pokazuje stan pusty.</summary>
    public void RemoveSource(string deviceId)
    {
        var idx = IndexOfSource(deviceId);
        if (idx < 0) return;
        bool wasSelected = SourceCombo.SelectedIndex == idx;
        if (wasSelected) _cts?.Cancel();
        _sources.RemoveAt(idx);
        if (_sources.Count == 0) ShowEmptyState();
        else if (wasSelected) SourceCombo.SelectedIndex = 0;
    }

    private int IndexOfSource(string id)
    {
        for (int i = 0; i < _sources.Count; i++)
            if (_sources[i].Id == id) return i;
        return -1;
    }

    private async void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => await LoadSelectedSourceAsync();

    private async Task LoadSelectedSourceAsync()
    {
        if (SourceCombo.SelectedItem is not DeviceInfo d) return;
        _cts?.Cancel();
        _device = d;
        _source = d.PhotoRoot ?? d.RootPath ?? "";
        SourceText.Text = _source;
        StatusText.ClearValue(TextBlock.ForegroundProperty);
        DestBox.Text = _config.ResolvedDestination(d.Id);
        _rows.Clear();
        ExtFilterPanel.Children.Clear();
        ExtFilterPanel.Visibility = Visibility.Collapsed;
        ClearPreview();
        await LoadAndAnalyzeAsync();
    }

    private void ShowEmptyState()
    {
        _device = null;
        _source = "";
        _rows.Clear();
        ExtFilterPanel.Children.Clear();
        ExtFilterPanel.Visibility = Visibility.Collapsed;
        ClearPreview();
        SourceText.Text = "";
        ImportButton.IsEnabled = false;
        Progress.Value = 0;
        StatusText.ClearValue(TextBlock.ForegroundProperty);
        StatusText.Text = L.Get("Msg_NoMedia");
        UpdateSelectionInfo();
    }

    private void ClearPreview()
    {
        PreviewImage.Source = null;
        PreviewName.Text = "";
        DetailsList.ItemsSource = null;
        PreviewHint.Visibility = Visibility.Visible;
    }

    private async Task LoadAndAnalyzeAsync()
    {
        StatusText.Text = L.Get("Msg_LoadingList");
        var exts = _config.ExtensionSet();

        // Szybko: tylko ścieżki + rozmiar/nazwa (bez EXIF) — lista pojawia się od razu.
        // Odczyt rozmiarów zrównoleglony (na wolnej karcie to skraca oczekiwanie).
        var rows = await Task.Run(() =>
            Importer.EnumeratePhotos(_source, exts).AsParallel().AsOrdered()
                .Select(p => new FileRow(p)).ToList());

        foreach (var r in rows) _rows.Add(r);
        BuildExtensionFilter();
        UpdateSelectionInfo();

        if (_rows.Count == 0)
        {
            StatusText.Text = L.Get("Msg_NoPhotos");
            return;
        }
        ImportButton.IsEnabled = true;

        // Dedup (po rozmiarze+nazwie — natychmiast), a daty EXIF dopełniamy równolegle w tle.
        await AnalyzeDuplicatesAsync();
        _ = FillDatesInBackgroundAsync();
    }

    /// <summary>Buduje przełączniki rozszerzeń obecnych na nośniku i filtruje po nich listę.</summary>
    private void BuildExtensionFilter()
    {
        var exts = _rows.Select(r => Path.GetExtension(r.Path))
            .Where(e => !string.IsNullOrEmpty(e))
            .Select(e => e.ToLowerInvariant())
            .Distinct().OrderBy(e => e).ToList();

        _enabledExts.Clear();
        foreach (var e in exts) _enabledExts.Add(e);

        _view = CollectionViewSource.GetDefaultView(_rows);
        _view.Filter = o => o is FileRow r && _enabledExts.Contains(Path.GetExtension(r.Path));

        ExtFilterPanel.Children.Clear();
        if (exts.Count > 1)
        {
            ExtFilterPanel.Children.Add(new TextBlock
            {
                Text = L.Get("Filter_Show"),
                Foreground = System.Windows.Media.Brushes.Gray,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0),
            });
            foreach (var e in exts)
            {
                var cb = new CheckBox
                {
                    Content = e,
                    IsChecked = true,
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                cb.Checked += ExtFilter_Changed;
                cb.Unchecked += ExtFilter_Changed;
                ExtFilterPanel.Children.Add(cb);
            }
            ExtFilterPanel.Visibility = Visibility.Visible;
        }
        else ExtFilterPanel.Visibility = Visibility.Collapsed;
    }

    private void ExtFilter_Changed(object sender, RoutedEventArgs e)
    {
        _enabledExts.Clear();
        foreach (var child in ExtFilterPanel.Children)
            if (child is CheckBox { IsChecked: true, Content: string ext })
                _enabledExts.Add(ext);
        _view?.Refresh();
        UpdateSelectionInfo();
    }

    private bool ExtEnabled(FileRow r) => _enabledExts.Contains(Path.GetExtension(r.Path));

    /// <summary>Szybka analiza „nowy/duplikat” (bez hashowania), w pełni anulowalna.</summary>
    private async Task AnalyzeDuplicatesAsync()
    {
        var dest = DestBox.Text.Trim();
        if (string.IsNullOrEmpty(dest))
        {
            foreach (var r in _rows) r.State = RowStatus.New;
            StatusText.Text = L.Get("Msg_PickDestForDup");
            UpdateSelectionInfo();
            return;
        }
        if (!Volumes.DriveAvailable(dest))
        {
            foreach (var r in _rows) r.State = RowStatus.Unknown;
            StatusText.Foreground = System.Windows.Media.Brushes.Firebrick;
            StatusText.Text = L.Get("Msg_LibDriveOffline", Volumes.DriveRoot(dest));
            UpdateSelectionInfo();
            return;
        }
        StatusText.ClearValue(TextBlock.ForegroundProperty);
        if (!Directory.Exists(dest))
        {
            foreach (var r in _rows) r.State = RowStatus.New;
            StatusText.Text = L.Get("Msg_DestNotExist");
            UpdateSelectionInfo();
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true);
        StatusText.Text = L.Get("Msg_AnalyzingDup");

        var byPath = _rows.ToDictionary(r => r.Path);
        var options = new ImportOptions
        {
            DestinationRoot = dest,
            FolderPattern = _config.FolderPattern,
        };
        var paths = _rows.Select(r => r.Path).ToList();

        var progress = new Progress<ImportProgress>(p =>
        {
            Progress.Value = p.Total == 0 ? 0 : 100.0 * p.Current / p.Total;
            if (byPath.TryGetValue(p.CurrentFile, out var row) && p.LastOutcome is { } outcome)
            {
                bool dup = outcome == ImportOutcome.SkippedDuplicate;
                row.State = dup ? RowStatus.Duplicate : RowStatus.New;
                if (dup) row.Selected = false;
            }
        });

        try
        {
            var importer = new Importer();
            await importer.AnalyzeAsync(paths, options, progress, token);

            Progress.Value = 0;
            int dupes = _rows.Count(r => r.State == RowStatus.Duplicate);
            StatusText.Text = dupes > 0
                ? L.Get("Msg_DoneDupes", dupes)
                : L.Get("Msg_DoneAllNew");
        }
        catch (OperationCanceledException)
        {
            Progress.Value = 0; // przełączenie źródła / zamknięcie — bez komunikatu (nowe wczytanie nadpisze)
        }
        finally
        {
            SetBusy(false);
            UpdateSelectionInfo();
        }
    }

    /// <summary>Uściśla wyświetlaną datę datą EXIF — równolegle, w tle, bez blokowania UI.</summary>
    private async Task FillDatesInBackgroundAsync()
    {
        var token = _cts?.Token ?? CancellationToken.None;
        var snapshot = _rows.ToList();
        try
        {
            await Parallel.ForEachAsync(
                snapshot,
                new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = token },
                async (row, ct) =>
                {
                    var date = await Task.Run(() => PhotoMetadata.GetCaptureDate(row.Path), ct);
                    await Dispatcher.InvokeAsync(() => row.Date = date);
                });
        }
        catch (OperationCanceledException) { /* okno zamknięte lub przerwane */ }
    }

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var dest = DestBox.Text.Trim();
        if (string.IsNullOrEmpty(dest))
        {
            MessageBox.Show(this, L.Get("Msg_PickDest"), "PhotoManager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Tylko zaznaczone i widoczne (przechodzące filtr rozszerzeń).
        var selected = _rows.Where(r => r.Selected && ExtEnabled(r)).Select(r => r.Path).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, L.Get("Msg_NoneSelected"), "PhotoManager",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var mode = MoveRadio.IsChecked == true ? ImportMode.Move : ImportMode.Copy;
        if (mode == ImportMode.Move)
        {
            var ok = MessageBox.Show(this,
                L.Get("Msg_ConfirmMove", selected.Count),
                L.Get("Title_ConfirmMove"), MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (ok != MessageBoxResult.Yes) return;
        }

        if (!Volumes.DriveAvailable(dest))
        {
            MessageBox.Show(this,
                L.Get("Msg_DestDriveOffline", Volumes.DriveRoot(dest)),
                "PhotoManager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try { Directory.CreateDirectory(dest); }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.Get("Msg_CantCreateDest", ex.Message), "PhotoManager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        // Zapamiętaj wybór dla aktualnego źródła na przyszłość (z serialem woluminu).
        if (_device is not null)
            _config.Devices[_device.Id] = new DeviceProfile
            {
                DisplayName = _device.Name,
                Destination = dest,
                DestinationSerial = Volumes.GetSerial(dest),
            };

        var options = new ImportOptions
        {
            DestinationRoot = dest,
            FolderPattern = _config.FolderPattern,
            Mode = mode,
            VerifyAfterCopy = _config.VerifyAfterCopy,
        };

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        SetBusy(true, allowClose: false);
        StatusText.Text = mode == ImportMode.Move ? L.Get("Msg_Moving") : L.Get("Msg_Copying");

        var targetState = mode == ImportMode.Move ? RowStatus.Moved : RowStatus.Copied;
        var byPath = _rows.ToDictionary(r => r.Path);
        var progress = new Progress<ImportProgress>(p =>
        {
            Progress.Value = p.Total == 0 ? 0 : 100.0 * p.Current / p.Total;
            StatusText.Text = $"{p.Current}/{p.Total} — {Path.GetFileName(p.CurrentFile)}";

            // Aktualizacja statusu w liście na bieżąco — widoczna także po przerwaniu.
            if (byPath.TryGetValue(p.CurrentFile, out var row) && p.LastOutcome is { } oc)
            {
                row.State = oc switch
                {
                    ImportOutcome.Imported => targetState,
                    ImportOutcome.SkippedDuplicate => RowStatus.Duplicate,
                    ImportOutcome.Failed => RowStatus.Error,
                    _ => row.State,
                };
                if (oc == ImportOutcome.Imported) row.Selected = false;
            }
        });

        ImportReport? report = null;
        bool cancelled = false;
        try
        {
            var importer = new Importer();
            report = await importer.ImportFilesAsync(selected, options, progress, token);
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            SetBusy(false);
            MessageBox.Show(this, L.Get("Msg_ImportError", ex.Message), "PhotoManager",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetBusy(false);

        if (cancelled || report is null)
        {
            Progress.Value = 0;
            int doneSoFar = _rows.Count(r => r.State == targetState);
            StatusText.Text = L.Get("Msg_ImportCancelled", doneSoFar, selected.Count);
            UpdateSelectionInfo();
            return;
        }

        Progress.Value = 100;
        StatusText.Text = L.Get("Msg_ImportSummaryStatus", report.Imported, report.Duplicates, report.Failed);

        MessageBox.Show(this,
            L.Get("Msg_ImportDoneBody", report.Imported, report.Duplicates, report.Failed, report.BytesImported / 1_048_576.0),
            L.Get("Title_ImportDone"), MessageBoxButton.OK, MessageBoxImage.Information);

        UpdateSelectionInfo();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    /// <summary>Po zaznaczeniu wiersza: załaduj miniaturę i szczegóły metadanych (poza wątkiem UI).</summary>
    private async void FilesGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        var row = FilesGrid.SelectedItem as FileRow;
        if (row is null)
        {
            PreviewImage.Source = null;
            PreviewName.Text = "";
            DetailsList.ItemsSource = null;
            PreviewHint.Visibility = Visibility.Visible;
            return;
        }

        PreviewName.Text = row.FileName;
        PreviewHint.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        DetailsList.ItemsSource = null;

        var path = row.Path;
        var thumb = await Task.Run(() => ThumbnailProvider.GetThumbnail(path, 512));
        var details = await Task.Run(() => PhotoMetadata.GetDetails(path));

        // Zaznaczenie mogło się zmienić, zanim dojechały dane — nie nadpisuj wtedy panelu.
        if (FilesGrid.SelectedItem as FileRow != row) return;

        PreviewImage.Source = thumb;
        if (thumb is null) PreviewHint.Visibility = Visibility.Visible;
        DetailsList.ItemsSource = details.Select(d => new DetailItem(L.Get("Meta_" + d.Label), d.Value)).ToList();
    }

    /// <summary>Dwuklik otwiera zdjęcie w skojarzonym programie.</summary>
    private void FilesGrid_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (FilesGrid.SelectedItem is not FileRow row) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(row.Path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, L.Get("Msg_CantOpenFile", ex.Message), "PhotoManager",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void ChangeDest_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = L.Get("Title_PickLibrary") };
        if (!string.IsNullOrEmpty(DestBox.Text) && Directory.Exists(DestBox.Text))
            dlg.InitialDirectory = DestBox.Text;
        if (dlg.ShowDialog() == true)
        {
            DestBox.Text = dlg.FolderName;
            await AnalyzeDuplicatesAsync();
        }
    }

    private void SelectNew_Click(object sender, RoutedEventArgs e)
    {
        // Zaznacza nowe tylko wśród widocznych (filtr rozszerzeń).
        foreach (var r in _rows)
            if (ExtEnabled(r))
                r.Selected = r.State is RowStatus.New or RowStatus.Pending;
        UpdateSelectionInfo();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close(); // Closing anuluje bieżącą operację

    private void SetBusy(bool busy, bool allowClose = true)
    {
        _busy = busy;
        CancelButton.IsEnabled = busy;
        ImportButton.IsEnabled = !busy && _rows.Count > 0;
        CloseButton.IsEnabled = !busy || allowClose;
        SourceCombo.IsEnabled = !busy; // nie przełączaj źródła w trakcie operacji
    }

    private void UpdateSelectionInfo()
    {
        var visible = _rows.Where(ExtEnabled).ToList();
        int sel = visible.Count(r => r.Selected);
        SelectionInfo.Text = L.Get("Msg_Selected", sel, visible.Count);
    }
}

/// <summary>Para etykieta–wartość w panelu szczegółów metadanych.</summary>
public sealed record DetailItem(string Label, string Value);
