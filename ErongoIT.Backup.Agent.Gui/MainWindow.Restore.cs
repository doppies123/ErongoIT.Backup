using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ErongoIT.Backup.Agent.Gui;

/// <summary>
/// CrashPlan-style restore: browse the PC's backed-up folders, tick files
/// and folders, pick a date/time ("as of"), and restore with options.
/// </summary>
public partial class MainWindow
{
    // Ticked items: path -> what it contains (for the totals line).
    private readonly Dictionary<string, RestoreSelection> _restoreSelected =
        new(StringComparer.Ordinal);

    private string _restoreFolder = string.Empty;
    private DateTime? _restoreAsOfUtc;            // null = most current
    private bool _restoreOpenedBefore;
    private bool _restoreLoading;
    private bool _restoreAsOfUpdating;
    private string? _restoreSearch;
    private CancellationTokenSource? _restoreCancellation;

    private sealed record RestoreSelection(
        bool IsFolder,
        int Files,
        int Folders,
        long Bytes);

    // ------------------------------------------------------------------
    // Opening the view
    // ------------------------------------------------------------------

    private async void Restore_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowView(RestoreView);

        if (_restoreOpenedBefore)
            return;

        _restoreOpenedBefore = true;

        InitializeAsOfControls();

        // Start where the files are: skip levels that hold a single folder
        // (This PC > C: > Users > Raymond), like CrashPlan.
        await LoadRestoreFolderAsync(string.Empty, autoDescend: true);
    }

    private void InitializeAsOfControls()
    {
        _restoreAsOfUpdating = true;

        RestoreAsOfHour.ItemsSource = Enumerable.Range(0, 24).Select(h => h.ToString("00")).ToList();
        RestoreAsOfMinute.ItemsSource = Enumerable.Range(0, 12).Select(m => (m * 5).ToString("00")).ToList();
        RestoreAsOfDate.DisplayDateEnd = DateTime.Today;

        ShowAsOf(null);

        _restoreAsOfUpdating = false;
    }

    private void ShowAsOf(DateTime? asOfUtc)
    {
        _restoreAsOfUpdating = true;

        var local = (asOfUtc ?? DateTime.UtcNow).ToLocalTime();

        RestoreAsOfDate.SelectedDate = local.Date;
        RestoreAsOfHour.SelectedItem = local.Hour.ToString("00");
        RestoreAsOfMinute.SelectedItem = (local.Minute / 5 * 5).ToString("00");

        _restoreAsOfUpdating = false;
    }

    // ------------------------------------------------------------------
    // Loading a folder or search results
    // ------------------------------------------------------------------

    private async Task LoadRestoreFolderAsync(
        string folder,
        bool autoDescend = false)
    {
        if (_restoreLoading)
            return;

        _restoreLoading = true;
        _restoreSearch = null;

        try
        {
            RestoreStatusText.Text = "Loading...";

            var includeDeleted = RestoreIncludeDeleted.IsChecked == true;

            var listing = await _api.BrowseAsync(
                _options.DeviceId,
                folder,
                _restoreAsOfUtc,
                includeDeleted);

            while (autoDescend &&
                   listing.Files.Count == 0 &&
                   listing.Folders.Count == 1)
            {
                listing = await _api.BrowseAsync(
                    _options.DeviceId,
                    listing.Folders[0].Path,
                    _restoreAsOfUtc,
                    includeDeleted);
            }

            _restoreFolder = listing.Folder;

            var rows = listing.Folders
                .Select(RestoreRow.FromFolder)
                .Concat(listing.Files.Select(RestoreRow.FromFile))
                .ToList();

            ApplySelectionState(rows);
            SetRestoreRows(rows);
            UpdateBreadcrumb(listing.Folder);

            RestoreStatusText.Text =
                rows.Count == 0
                    ? _restoreAsOfUtc is null
                        ? "No backed-up files here yet. The first backup with the new version fills this list."
                        : "No backed-up files here at that date and time."
                    : $"{listing.Folders.Count:N0} folder(s), {listing.Files.Count:N0} file(s) " +
                      $"• {DescribeAsOf()}";
        }
        catch (Exception ex)
        {
            RestoreStatusText.Text = $"Unable to load files: {ex.Message}";
        }
        finally
        {
            _restoreLoading = false;
        }
    }

    private async Task LoadRestoreSearchAsync(string text)
    {
        if (_restoreLoading)
            return;

        _restoreLoading = true;

        try
        {
            RestoreStatusText.Text = $"Searching for \"{text}\"...";

            var results = await _api.SearchAsync(
                _options.DeviceId,
                text,
                _restoreAsOfUtc,
                RestoreIncludeDeleted.IsChecked == true);

            _restoreSearch = text;

            var rows = results.Select(RestoreRow.FromFile).ToList();

            foreach (var row in rows)
                row.ShowFolderInName();

            ApplySelectionState(rows);
            SetRestoreRows(rows);

            RestoreBreadcrumb.ItemsSource = new[]
            {
                new BreadcrumbItem("This PC", string.Empty),
                new BreadcrumbItem($"Search results for \"{text}\"  (click to go back to {LastSegment(_restoreFolder)})", _restoreFolder)
            };

            RestoreStatusText.Text =
                rows.Count == 0
                    ? $"No files named like \"{text}\"."
                    : $"{rows.Count:N0} file(s) found{(rows.Count >= 1000 ? " (first 1,000 shown)" : string.Empty)} • {DescribeAsOf()}";
        }
        catch (Exception ex)
        {
            RestoreStatusText.Text = $"Search failed: {ex.Message}";
        }
        finally
        {
            _restoreLoading = false;
        }
    }

    private Task ReloadRestoreAsync() =>
        _restoreSearch is { Length: > 0 } search
            ? LoadRestoreSearchAsync(search)
            : LoadRestoreFolderAsync(_restoreFolder);

    private void UpdateBreadcrumb(string folder)
    {
        var items = new List<BreadcrumbItem> { new("This PC", string.Empty) };

        if (folder.Length > 0)
        {
            var path = string.Empty;

            foreach (var segment in folder.Split('/'))
            {
                path = path.Length == 0 ? segment : $"{path}/{segment}";
                items.Add(new BreadcrumbItem(segment, path));
            }
        }

        RestoreBreadcrumb.ItemsSource = items;
    }

    private string DescribeAsOf() =>
        _restoreAsOfUtc is null
            ? "most current versions"
            : $"as of {_restoreAsOfUtc.Value.ToLocalTime():dd MMM yyyy HH:mm}";

    private static string LastSegment(string path) =>
        path.Length == 0 ? "This PC" : path[(path.LastIndexOf('/') + 1)..];

    // ------------------------------------------------------------------
    // Toolbar events
    // ------------------------------------------------------------------

    private async void RestoreBreadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
            await LoadRestoreFolderAsync(path);
    }

    private async void RestoreFilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (RestoreFilesGrid.SelectedItem is RestoreRow { IsFolder: true } row)
            await LoadRestoreFolderAsync(row.Path);
    }

    private async void RestoreSearch_Click(object sender, RoutedEventArgs e)
    {
        var text = RestoreSearchText.Text.Trim();

        if (text.Length == 0)
            await LoadRestoreFolderAsync(_restoreFolder);
        else
            await LoadRestoreSearchAsync(text);
    }

    private void RestoreSearchText_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            RestoreSearch_Click(sender, e);
    }

    private async void RestoreAsOf_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_restoreAsOfUpdating ||
            RestoreAsOfDate.SelectedDate is not DateTime date ||
            RestoreAsOfHour.SelectedItem is not string hour ||
            RestoreAsOfMinute.SelectedItem is not string minute)
        {
            return;
        }

        // End of the chosen minute, in local time -> UTC.
        var local = date.Date
            .AddHours(int.Parse(hour))
            .AddMinutes(int.Parse(minute))
            .AddSeconds(59);

        _restoreAsOfUtc = local >= DateTime.Now
            ? null
            : DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();

        await ReloadRestoreAsync();
    }

    private async void RestoreMostCurrent_Click(object sender, RoutedEventArgs e)
    {
        _restoreAsOfUtc = null;
        ShowAsOf(null);
        await ReloadRestoreAsync();
    }

    private async void RestoreIncludeDeleted_Changed(object sender, RoutedEventArgs e)
    {
        if (_restoreOpenedBefore)
            await ReloadRestoreAsync();
    }

    // ------------------------------------------------------------------
    // Ticking files and folders
    // ------------------------------------------------------------------

    private void RestoreRowCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: RestoreRow row })
            return;

        if (_restoreSelected.ContainsKey(row.Path))
        {
            _restoreSelected.Remove(row.Path);
        }
        else
        {
            // A ticked folder includes everything inside it.
            if (row.IsFolder)
            {
                var prefix = row.Path + "/";

                foreach (var key in _restoreSelected.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                    _restoreSelected.Remove(key);
            }

            _restoreSelected[row.Path] = new RestoreSelection(
                row.IsFolder,
                row.IsFolder ? row.FileCount : 1,
                row.IsFolder ? row.FolderCount + 1 : 0,
                row.SizeBytes);
        }

        ApplySelectionState(CurrentRestoreRows());
        UpdateRestoreSelectionText();
    }

    private void RestoreClearSelection_Click(object sender, RoutedEventArgs e)
    {
        _restoreSelected.Clear();
        ApplySelectionState(CurrentRestoreRows());
        UpdateRestoreSelectionText();
    }

    private IEnumerable<RestoreRow> CurrentRestoreRows() =>
        RestoreFilesGrid.ItemsSource as IEnumerable<RestoreRow> ?? Array.Empty<RestoreRow>();

    private void ApplySelectionState(IEnumerable<RestoreRow> rows)
    {
        foreach (var row in rows)
        {
            var direct = _restoreSelected.ContainsKey(row.Path);
            var inherited = !direct && _restoreSelected.Keys.Any(k => row.Path.StartsWith(k + "/", StringComparison.Ordinal));

            row.SetChecked(direct || inherited, canToggle: !inherited);
        }
    }

    private void UpdateRestoreSelectionText()
    {
        if (_restoreSelected.Count == 0)
        {
            RestoreSelectionText.Text = "Nothing selected";
            RestoreBackupButton.IsEnabled = false;
            return;
        }

        var folders = _restoreSelected.Values.Sum(s => s.Folders);
        var files = _restoreSelected.Values.Sum(s => s.Files);
        var bytes = _restoreSelected.Values.Sum(s => s.Bytes);

        RestoreSelectionText.Text =
            $"Folders: {folders:N0}   Files: {files:N0}   Total size: {FormatBytes(bytes)}";

        RestoreBackupButton.IsEnabled = !_restoreRunning;
    }

    // ------------------------------------------------------------------
    // Restoring
    // ------------------------------------------------------------------

    private bool _restoreRunning;

    private void RestoreCancel_Click(object sender, RoutedEventArgs e)
    {
        _restoreCancellation?.Cancel();
    }

    private async void RestoreFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_restoreSelected.Count == 0 || _restoreRunning)
            return;

        var dialog = new RestoreOptionsWindow { Owner = this };

        if (dialog.ShowDialog() != true)
            return;

        var options = dialog.Result;
        var includeDeleted = RestoreIncludeDeleted.IsChecked == true;
        var asOf = _restoreAsOfUtc;
        var selectedPaths = _restoreSelected.Keys.ToList();

        _restoreRunning = true;
        _restoreCancellation = new CancellationTokenSource();
        var token = _restoreCancellation.Token;

        RestoreBackupButton.IsEnabled = false;
        RestoreCancelButton.Visibility = Visibility.Visible;
        RestoreProgress.Visibility = Visibility.Visible;
        RestoreProgress.Value = 0;

        int restored = 0, skipped = 0, failed = 0;
        long bytes = 0;
        var errors = new List<string>();

        try
        {
            RestoreStatusText.Text = "Preparing the list of files...";

            var versions = await _api.ResolveAsync(
                _options.DeviceId,
                selectedPaths,
                asOf,
                includeDeleted,
                token);

            if (versions.Count == 0)
            {
                RestoreStatusText.Text = "The selection contains no files.";
                return;
            }

            var basePath = CommonParent(selectedPaths);
            var total = versions.Count;
            var index = 0;

            foreach (var version in versions)
            {
                token.ThrowIfCancellationRequested();
                index++;

                RestoreProgress.Value = index * 100.0 / total;
                RestoreStatusText.Text = $"Restoring {index:N0} of {total:N0}: {version.Name}";

                string target;

                try
                {
                    target = options.TargetPath(version.Path, basePath);
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{version.Path}: {ex.Message}");
                    continue;
                }

                try
                {
                    if (File.Exists(target))
                    {
                        switch (options.Existing)
                        {
                            case ExistingFileAction.Skip:
                                skipped++;
                                continue;

                            case ExistingFileAction.RenameExisting:
                                File.Move(target, RestoreOptions.RenamedPath(target));
                                break;

                            case ExistingFileAction.Overwrite:
                                break;
                        }
                    }

                    bytes += await _api.DownloadVersionAsync(version.VersionId, target, token);

                    if (version.LastWriteUtc is DateTime lastWrite)
                    {
                        try
                        {
                            File.SetLastWriteTimeUtc(target, lastWrite);
                        }
                        catch
                        {
                            // Not important enough to fail the restore.
                        }
                    }

                    restored++;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    errors.Add($"{version.Path}: {ex.Message}");
                }
            }

            RestoreStatusText.Text =
                $"Restore finished: {restored:N0} restored ({FormatBytes(bytes)}), {skipped:N0} skipped, {failed:N0} failed.";

            var message =
                $"Restored {restored:N0} file(s) ({FormatBytes(bytes)}).\n" +
                (skipped > 0 ? $"Skipped {skipped:N0} existing file(s).\n" : string.Empty) +
                (failed > 0 ? $"Failed {failed:N0} file(s):\n\n" + string.Join("\n", errors.Take(10)) +
                              (errors.Count > 10 ? $"\n... and {errors.Count - 10:N0} more" : string.Empty)
                            : string.Empty) +
                $"\n\nRestored to: {options.Describe(basePath)}";

            MessageBox.Show(
                this,
                message,
                "Restore",
                MessageBoxButton.OK,
                failed > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            RestoreStatusText.Text =
                $"Restore stopped: {restored:N0} restored, {skipped:N0} skipped, {failed:N0} failed.";
        }
        catch (Exception ex)
        {
            RestoreStatusText.Text = $"Restore failed: {ex.Message}";
        }
        finally
        {
            _restoreRunning = false;
            _restoreCancellation?.Dispose();
            _restoreCancellation = null;

            RestoreCancelButton.Visibility = Visibility.Collapsed;
            RestoreProgress.Visibility = Visibility.Collapsed;

            UpdateRestoreSelectionText();
        }
    }

    /// <summary>
    /// Deepest folder that contains every selected item. Restoring to a
    /// different folder keeps the structure below it.
    /// </summary>
    private static string CommonParent(IReadOnlyList<string> paths)
    {
        var parents = paths
            .Select(p => p.Contains('/') ? p[..p.LastIndexOf('/')] : string.Empty)
            .Select(p => p.Length == 0 ? Array.Empty<string>() : p.Split('/'))
            .ToList();

        if (parents.Count == 0)
            return string.Empty;

        var common = parents[0].ToList();

        foreach (var segments in parents.Skip(1))
        {
            var length = 0;

            while (length < common.Count &&
                   length < segments.Length &&
                   string.Equals(common[length], segments[length], StringComparison.Ordinal))
            {
                length++;
            }

            common = common.Take(length).ToList();
        }

        return string.Join('/', common);
    }

    // ------------------------------------------------------------------

    public sealed record BreadcrumbItem(string Name, string Path);
}

/// <summary>One line in the restore list: a folder or a file.</summary>
public sealed class RestoreRow : INotifyPropertyChanged
{
    private static readonly Brush NormalBrush = Frozen(Color.FromRgb(0x20, 0x24, 0x2A));
    private static readonly Brush DeletedBrush = Frozen(Color.FromRgb(0x9A, 0xA1, 0xA9));

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    public bool IsFolder { get; private init; }
    public string Name { get; private set; } = string.Empty;
    public string Path { get; private init; } = string.Empty;
    public Guid? VersionId { get; private init; }
    public long SizeBytes { get; private init; }
    public int FileCount { get; private init; }
    public int FolderCount { get; private init; }
    public DateTime? LastWriteUtc { get; private init; }
    public bool Deleted { get; private init; }

    public bool IsChecked { get; private set; }
    public bool CanToggle { get; private set; } = true;

    public string Icon => IsFolder ? "📁" : "📄";

    public string SortName => (IsFolder ? "0" : "1") + Name;

    public string SizeDisplay =>
        IsFolder ? $"{FileCount:N0} files" : FormatSize(SizeBytes);

    public string ModifiedDisplay =>
        LastWriteUtc is null ? "—" : LastWriteUtc.Value.ToLocalTime().ToString("dd/MM/yy hh:mm tt");

    public DateTime ModifiedSort => LastWriteUtc ?? DateTime.MinValue;

    public Brush NameBrush => Deleted ? DeletedBrush : NormalBrush;

    public FontStyle NameStyle => Deleted ? FontStyles.Italic : FontStyles.Normal;

    public event PropertyChangedEventHandler? PropertyChanged;

    public static RestoreRow FromFolder(FolderItemDto folder) => new()
    {
        IsFolder = true,
        Name = folder.Name + (folder.Deleted ? "  (deleted)" : string.Empty),
        Path = folder.Path,
        SizeBytes = folder.SizeBytes,
        FileCount = folder.FileCount,
        FolderCount = folder.FolderCount,
        LastWriteUtc = folder.LastWriteUtc,
        Deleted = folder.Deleted
    };

    public static RestoreRow FromFile(VersionItemDto file) => new()
    {
        IsFolder = false,
        Name = file.Name + (file.Deleted ? "  (deleted)" : string.Empty),
        Path = file.Path,
        VersionId = file.VersionId,
        SizeBytes = file.SizeBytes,
        FileCount = 1,
        LastWriteUtc = file.LastWriteUtc,
        Deleted = file.Deleted
    };

    /// <summary>Search results: show where the file lives.</summary>
    public void ShowFolderInName()
    {
        var slash = Path.LastIndexOf('/');

        if (slash > 0)
            Name = $"{Name}   —   {Path[..slash].Replace('/', '\\')}";
    }

    public void SetChecked(bool isChecked, bool canToggle)
    {
        if (IsChecked == isChecked && CanToggle == canToggle)
            return;

        IsChecked = isChecked;
        CanToggle = canToggle;

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanToggle)));
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:0} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):0.0} MB";
        return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
    }
}
