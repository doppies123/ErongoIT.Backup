namespace ErongoIT.Backup.Agent.Gui;

public sealed class GuiOptions
{
    public string ApiBaseUrl { get; set; } =
        "http://localhost:5230";

    /// <summary>Developer profiles only.</summary>
    public string Username { get; set; } =
        string.Empty;

    public string Password { get; set; } =
        string.Empty;

    /// <summary>Enrolled PCs: the device key from agent.json.</summary>
    public string DeviceKey { get; set; } =
        string.Empty;

    public Guid CustomerId { get; set; }

    public Guid DeviceId { get; set; }

    public string SourcePath { get; set; } =
        string.Empty;

    public List<string> SourcePaths { get; set; } = new();

    public bool IsEnrolled =>
        !string.IsNullOrWhiteSpace(DeviceKey);

    public IReadOnlyList<string> GetSourceFolders()
    {
        var folders = new List<string>();

        foreach (var folder in SourcePaths.Append(SourcePath))
        {
            if (!string.IsNullOrWhiteSpace(folder) &&
                !folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                folders.Add(folder);
            }
        }

        return folders;
    }
}
