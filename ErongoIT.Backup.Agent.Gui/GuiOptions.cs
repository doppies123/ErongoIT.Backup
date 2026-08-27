namespace ErongoIT.Backup.Agent.Gui;

public sealed class GuiOptions
{
    public string ApiBaseUrl { get; set; } =
        "http://localhost:5230";

    public Guid CustomerId { get; set; }

    public Guid DeviceId { get; set; }

    public string SourcePath { get; set; } =
        string.Empty;
}
