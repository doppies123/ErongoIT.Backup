using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ErongoIT.Backup.Agent.Gui;

public enum RestoreDestinationKind
{
    OriginalLocation,
    Desktop,
    Downloads,
    Folder
}

public enum ExistingFileAction
{
    RenameExisting,
    Overwrite,
    Skip
}

/// <summary>Where restored files go and what happens to files already there.</summary>
public sealed record RestoreOptions(
    RestoreDestinationKind Destination,
    string? FolderPath,
    ExistingFileAction Existing)
{
    /// <summary>
    /// Local path for a backed-up file.
    /// Original location: "C:/Users/Ray/a.txt" -> "C:\Users\Ray\a.txt".
    /// Elsewhere: the structure below the selection's common folder is kept,
    /// e.g. ticking "C:/Data/Docs" restores into "Downloads\Docs\...".
    /// </summary>
    public string TargetPath(string serverPath, string basePath)
    {
        var segments = serverPath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (segments.Any(s => s is "." or ".."))
            throw new InvalidOperationException("Invalid path in backup.");

        if (Destination == RestoreDestinationKind.OriginalLocation)
        {
            if (segments.Length < 2 || !IsDrive(segments[0]))
                throw new InvalidOperationException("The original location is unknown for this file.");

            return segments[0] + "\\" + string.Join('\\', segments.Skip(1));
        }

        var relative = basePath.Length > 0 &&
                       serverPath.StartsWith(basePath + "/", StringComparison.Ordinal)
            ? serverPath[(basePath.Length + 1)..]
            : serverPath.Replace(":", string.Empty);

        var root = RootFolder();
        var target = Path.GetFullPath(Path.Combine(root, relative.Replace('/', '\\')));

        // Never write outside the chosen folder.
        if (!target.StartsWith(Path.GetFullPath(root).TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The file would be restored outside the chosen folder.");

        return target;
    }

    public string Describe(string basePath) =>
        Destination == RestoreDestinationKind.OriginalLocation
            ? "the original location"
            : RootFolder();

    private string RootFolder() => Destination switch
    {
        RestoreDestinationKind.Desktop =>
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),

        RestoreDestinationKind.Downloads =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),

        _ => FolderPath ?? throw new InvalidOperationException("No restore folder was chosen.")
    };

    /// <summary>"report.docx" -> "report (before restore 2026-10-07 15.30).docx" (unique).</summary>
    public static string RenamedPath(string path)
    {
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        var stamp = DateTime.Now.ToString("yyyy-MM-dd HH.mm");

        var candidate = Path.Combine(folder, $"{name} (before restore {stamp}){extension}");
        var counter = 2;

        while (File.Exists(candidate))
            candidate = Path.Combine(folder, $"{name} (before restore {stamp} {counter++}){extension}");

        return candidate;
    }

    private static bool IsDrive(string segment) =>
        segment.Length == 2 && char.IsLetter(segment[0]) && segment[1] == ':';
}

/// <summary>"Restore Files Options" dialog (like CrashPlan's).</summary>
public sealed class RestoreOptionsWindow : Window
{
    private readonly ComboBox _destination = new() { Height = 30, MinWidth = 260 };
    private readonly ComboBox _existing = new() { Height = 30, MinWidth = 260 };
    private readonly TextBlock _folderText = new()
    {
        Foreground = System.Windows.Media.Brushes.DimGray,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 6, 0, 0)
    };

    private string? _chosenFolder;
    private int _lastDestinationIndex = 2;

    public RestoreOptions Result { get; private set; } =
        new(RestoreDestinationKind.Downloads, null, ExistingFileAction.RenameExisting);

    public RestoreOptionsWindow()
    {
        Title = "Restore Files Options";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        _destination.ItemsSource = new[]
        {
            "Original location",
            "Desktop",
            "Downloads",
            "Choose a folder..."
        };
        _destination.SelectedIndex = 2;
        _destination.SelectionChanged += Destination_SelectionChanged;

        _existing.ItemsSource = new[]
        {
            "Rename existing file and restore",
            "Overwrite existing file",
            "Keep existing file (skip)"
        };
        _existing.SelectedIndex = 0;

        var grid = new Grid { Margin = new Thickness(24) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        for (var i = 0; i < 4; i++)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        AddRow(grid, 0, "Save selected files to:", _destination);
        Grid.SetRow(_folderText, 1);
        Grid.SetColumnSpan(_folderText, 2);
        grid.Children.Add(_folderText);
        AddRow(grid, 2, "When restoring over existing files:", _existing);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 22, 0, 0)
        };

        var cancel = new Button { Content = "Cancel", Padding = new Thickness(18, 6, 18, 6), IsCancel = true };
        var go = new Button
        {
            Content = "Go",
            Padding = new Thickness(24, 6, 24, 6),
            Margin = new Thickness(10, 0, 0, 0),
            IsDefault = true
        };

        go.Click += Go_Click;

        buttons.Children.Add(cancel);
        buttons.Children.Add(go);

        Grid.SetRow(buttons, 3);
        Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);

        Content = grid;

        UpdateFolderText();
    }

    private static void AddRow(Grid grid, int row, string label, Control control)
    {
        var text = new TextBlock
        {
            Text = label,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 8, 16, 8)
        };

        control.Margin = new Thickness(0, 8, 0, 8);

        Grid.SetRow(text, row);
        Grid.SetRow(control, row);
        Grid.SetColumn(control, 1);

        grid.Children.Add(text);
        grid.Children.Add(control);
    }

    private void Destination_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_destination.SelectedIndex == 3)
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose where to restore the files"
            };

            if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
            {
                _chosenFolder = dialog.FolderName;
            }
            else if (_chosenFolder is null)
            {
                _destination.SelectedIndex = _lastDestinationIndex;
                return;
            }
        }

        _lastDestinationIndex = _destination.SelectedIndex;
        UpdateFolderText();
    }

    private void UpdateFolderText()
    {
        _folderText.Text = _destination.SelectedIndex switch
        {
            0 => "Files go back where they came from (for example C:\\Users\\...).",
            1 => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            2 => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            _ => _chosenFolder ?? string.Empty
        };
    }

    private void Go_Click(object sender, RoutedEventArgs e)
    {
        var destination = (RestoreDestinationKind)Math.Max(0, _destination.SelectedIndex);
        var existing = (ExistingFileAction)Math.Max(0, _existing.SelectedIndex);

        if (destination == RestoreDestinationKind.Folder && string.IsNullOrWhiteSpace(_chosenFolder))
        {
            MessageBox.Show(this, "Choose a folder first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (destination == RestoreDestinationKind.OriginalLocation &&
            existing == ExistingFileAction.Overwrite &&
            MessageBox.Show(
                this,
                "Files in their original location will be replaced by the backed-up versions. Continue?",
                Title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
        {
            return;
        }

        Result = new RestoreOptions(destination, _chosenFolder, existing);
        DialogResult = true;
    }
}
