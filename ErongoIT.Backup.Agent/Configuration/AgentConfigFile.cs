using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ErongoIT.Backup.Agent.Configuration;

/// <summary>
/// Machine-wide Agent configuration written at enrollment:
///   C:\ProgramData\ErongoIT Backup\agent.json
///
/// The device key is encrypted with Windows DPAPI (LocalMachine scope),
/// so the file is useless if copied to another computer.
/// When this file exists it overrides appsettings.json.
/// </summary>
public sealed class AgentConfigFile
{
    public const string ServiceName = "ErongoIT Backup Agent";

    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("ErongoIT.Backup.DeviceKey.v1");

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string DataDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ErongoIT Backup");

    public static string ConfigPath =>
        Path.Combine(DataDirectory, "agent.json");

    public static string LogDirectory =>
        Path.Combine(DataDirectory, "logs");

    public string ApiBaseUrl { get; set; } = string.Empty;

    public Guid CustomerId { get; set; }

    public Guid DeviceId { get; set; }

    public string DeviceName { get; set; } = string.Empty;

    public string DeviceKeyProtected { get; set; } = string.Empty;

    public List<string> SourcePaths { get; set; } = new();

    public DateTime EnrolledAtUtc { get; set; }

    public static bool Exists => File.Exists(ConfigPath);

    public static AgentConfigFile? TryLoad()
    {
        try
        {
            if (!File.Exists(ConfigPath))
                return null;

            return JsonSerializer.Deserialize<AgentConfigFile>(
                File.ReadAllText(ConfigPath),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DataDirectory);

        var temporaryPath = ConfigPath + ".tmp";

        File.WriteAllText(
            temporaryPath,
            JsonSerializer.Serialize(this, JsonOptions));

        File.Move(temporaryPath, ConfigPath, overwrite: true);
    }

    public void SetDeviceKey(string deviceKey)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(deviceKey),
            Entropy,
            DataProtectionScope.LocalMachine);

        DeviceKeyProtected = Convert.ToBase64String(protectedBytes);
    }

    public string GetDeviceKey()
    {
        if (string.IsNullOrWhiteSpace(DeviceKeyProtected))
            return string.Empty;

        var bytes = ProtectedData.Unprotect(
            Convert.FromBase64String(DeviceKeyProtected),
            Entropy,
            DataProtectionScope.LocalMachine);

        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Overlays the enrolled settings onto the Agent options.</summary>
    public void ApplyTo(AgentOptions options)
    {
        if (!string.IsNullOrWhiteSpace(ApiBaseUrl))
            options.ApiBaseUrl = ApiBaseUrl;

        if (CustomerId != Guid.Empty)
            options.CustomerId = CustomerId;

        if (DeviceId != Guid.Empty)
            options.DeviceId = DeviceId;

        var key = GetDeviceKey();

        if (!string.IsNullOrWhiteSpace(key))
        {
            options.DeviceKey = key;

            // Enrolled PCs never use admin credentials.
            options.ApiUsername = string.Empty;
            options.ApiPassword = string.Empty;
        }

        if (SourcePaths.Count > 0)
        {
            options.SourcePaths = new List<string>(SourcePaths);
            options.SourcePath = string.Empty;
        }
    }
}
