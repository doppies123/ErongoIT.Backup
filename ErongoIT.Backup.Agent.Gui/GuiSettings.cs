using System.IO;
using System.Text.Json;

namespace ErongoIT.Backup.Agent.Gui;

/// <summary>
/// Per-user display settings for the Agent GUI, stored in
/// %LOCALAPPDATA%\ErongoIT.Backup\gui-settings.json.
/// (Not in ProgramData: changing them never needs admin rights.)
/// </summary>
public sealed class GuiSettings
{
    public static readonly int[] RowsPerPageChoices = { 25, 50, 100, 250, 500 };

    public const int DefaultRowsPerPage = 50;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { WriteIndented = true };

    public int RowsPerPage { get; set; } = DefaultRowsPerPage;

    /// <summary>Last update offered with a popup, and when ("Later" = ask again next day).</summary>
    public string? UpdatePromptedVersion { get; set; }

    public DateTime? UpdatePromptedUtc { get; set; }

    private static string SettingsPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ErongoIT.Backup",
            "gui-settings.json");

    public static GuiSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<GuiSettings>(
                    File.ReadAllText(SettingsPath),
                    JsonOptions);

                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch
        {
            // Unreadable settings: use defaults.
        }

        return new GuiSettings();
    }

    public void Save()
    {
        try
        {
            Normalize();
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            // Display settings are a convenience; never fail the GUI over them.
        }
    }

    private void Normalize()
    {
        if (!RowsPerPageChoices.Contains(RowsPerPage))
            RowsPerPage = DefaultRowsPerPage;
    }
}

/// <summary>Simple page calculator for a list shown in pages.</summary>
public sealed class PagedList<T>
{
    private IReadOnlyList<T> _items = Array.Empty<T>();

    public int PageSize { get; private set; } = GuiSettings.DefaultRowsPerPage;

    public int PageIndex { get; private set; }

    public int TotalCount => _items.Count;

    public int PageCount =>
        Math.Max(1, (int)Math.Ceiling(_items.Count / (double)PageSize));

    public void SetItems(IReadOnlyList<T> items, bool keepPage = false)
    {
        _items = items ?? Array.Empty<T>();
        PageIndex = keepPage ? Math.Min(PageIndex, PageCount - 1) : 0;
    }

    public void SetPageSize(int pageSize)
    {
        // Keep the first visible row on screen when the size changes.
        var firstRow = PageIndex * PageSize;
        PageSize = Math.Max(1, pageSize);
        PageIndex = Math.Min(firstRow / PageSize, PageCount - 1);
    }

    public void Move(string? where)
    {
        PageIndex = where switch
        {
            "first" => 0,
            "previous" => Math.Max(0, PageIndex - 1),
            "next" => Math.Min(PageCount - 1, PageIndex + 1),
            "last" => PageCount - 1,
            _ => PageIndex
        };
    }

    public IReadOnlyList<T> CurrentPage =>
        _items
            .Skip(PageIndex * PageSize)
            .Take(PageSize)
            .ToList();

    public string Describe(string noun)
    {
        if (TotalCount == 0)
            return $"No {noun}";

        var from = PageIndex * PageSize + 1;
        var to = Math.Min(TotalCount, from + PageSize - 1);

        return $"Showing {from:N0}–{to:N0} of {TotalCount:N0} {noun}";
    }
}
