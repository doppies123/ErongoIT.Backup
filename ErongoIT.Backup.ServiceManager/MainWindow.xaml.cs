using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ErongoIT.Backup.ServiceManager;

public partial class MainWindow : Window
{
    private const string DefaultCloudUrl = "https://backup.erongoit.com";

    private static readonly Brush Green = new SolidColorBrush(Color.FromRgb(0x2E, 0xB8, 0x4F));
    private static readonly Brush Red = new SolidColorBrush(Color.FromRgb(0xD6, 0x28, 0x28));
    private static readonly Brush Amber = new SolidColorBrush(Color.FromRgb(0xE8, 0xA3, 0x17));
    private static readonly Brush Grey = new SolidColorBrush(Color.FromRgb(0x9A, 0xA3, 0xAD));

    private readonly string _rootPath;
    private readonly string _logPath;
    private readonly DispatcherTimer _localTimer;
    private readonly DispatcherTimer _cloudTimer;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    private AgentSettings _settings;
    private string? _accessToken;
    private DateTime _accessTokenExpiresUtc = DateTime.MinValue;
    private bool _cloudRefreshing;

    public MainWindow()
    {
        InitializeComponent();

        _rootPath = FindRepositoryRoot();
        _logPath = System.IO.Path.Combine(_rootPath, "logs");
        Directory.CreateDirectory(_logPath);

        _settings = LoadAgentSettings(_rootPath);
        CloudUrlText.Text = _settings.ApiBaseUrl;

        // Local processes: cheap, every 2 seconds.
        _localTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _localTimer.Tick += (_, _) => RefreshLocalStatus();

        // Cloud server + backup status: every 30 seconds.
        _cloudTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _cloudTimer.Tick += async (_, _) => await RefreshCloudStatusAsync();

        Loaded += async (_, _) =>
        {
            RefreshLocalStatus();
            _localTimer.Start();
            _cloudTimer.Start();
            await RefreshCloudStatusAsync();
        };

        Closing += (_, _) =>
        {
            _localTimer.Stop();
            _cloudTimer.Stop();
            _http.Dispose();
        };
    }

    // ------------------------------------------------------------------
    // Settings
    // ------------------------------------------------------------------

    private sealed record AgentSettings(
        string ApiBaseUrl,
        string? Username,
        string? Password,
        Guid DeviceId)
    {
        public bool HasCredentials =>
            !string.IsNullOrWhiteSpace(Username) &&
            !string.IsNullOrWhiteSpace(Password) &&
            DeviceId != Guid.Empty;
    }

    /// <summary>
    /// Uses the background Agent's own settings file, so the Service
    /// Manager always watches the same server and device as the Agent.
    /// </summary>
    private static AgentSettings LoadAgentSettings(string rootPath)
    {
        var url = DefaultCloudUrl;
        string? username = null;
        string? password = null;
        var deviceId = Guid.Empty;

        foreach (var file in new[] { "appsettings.json", "appsettings.Development.json" })
        {
            var path = System.IO.Path.Combine(rootPath, "ErongoIT.Backup.Agent", file);

            if (!File.Exists(path))
                continue;

            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));

                if (!document.RootElement.TryGetProperty("Agent", out var agent))
                    continue;

                if (TryGetString(agent, "ApiBaseUrl", out var value))
                    url = value.TrimEnd('/');

                if (TryGetString(agent, "ApiUsername", out value))
                    username = value;

                if (TryGetString(agent, "ApiPassword", out value))
                    password = value;

                if (TryGetString(agent, "DeviceId", out value) &&
                    Guid.TryParse(value, out var parsed))
                {
                    deviceId = parsed;
                }
            }
            catch
            {
                // Keep defaults.
            }
        }

        return new AgentSettings(url, username, password, deviceId);
    }

    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(name, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var text = property.GetString();

        if (string.IsNullOrWhiteSpace(text))
            return false;

        value = text.Trim();
        return true;
    }

    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            var current = new DirectoryInfo(start);

            while (current is not null)
            {
                if (File.Exists(System.IO.Path.Combine(current.FullName, "docker-compose.yml")) &&
                    File.Exists(System.IO.Path.Combine(current.FullName, "ErongoIT.Backup.Api", "ErongoIT.Backup.Api.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the ErongoIT.Backup repository root.");
    }

    // ------------------------------------------------------------------
    // Status
    // ------------------------------------------------------------------

    private async void RefreshStatus_Click(object sender, RoutedEventArgs e)
    {
        _settings = LoadAgentSettings(_rootPath);
        CloudUrlText.Text = _settings.ApiBaseUrl;
        _accessToken = null;

        RefreshLocalStatus();
        await RefreshCloudStatusAsync();
    }

    private void RefreshLocalStatus()
    {
        var agentRunning = IsProjectRunning("ErongoIT.Backup.Agent");
        SetLed(AgentLed, AgentStatusText, agentRunning ? Green : Red, agentRunning ? "Running" : "Stopped");

        var apiRunning = IsPortOpen(5230);
        SetLed(ApiLed, ApiStatusText, apiRunning ? Green : Grey, apiRunning ? "Running" : "Stopped");

        NextBackupText.Text = agentRunning
            ? ReadNextRunFromAgentLog() ?? "Waiting for the Agent to check its schedule..."
            : "Agent is stopped — no scheduled backups will run.";
    }

    private async Task RefreshCloudStatusAsync()
    {
        if (_cloudRefreshing)
            return;

        _cloudRefreshing = true;

        try
        {
            // 1. Server health (no login needed).
            var healthy = false;

            try
            {
                using var response = await _http.GetAsync($"{_settings.ApiBaseUrl}/api/health");
                var body = await response.Content.ReadAsStringAsync();

                healthy = response.IsSuccessStatusCode &&
                          body.Contains("\"Healthy\"", StringComparison.OrdinalIgnoreCase);

                SetLed(
                    CloudLed,
                    CloudStatusText,
                    healthy ? Green : Amber,
                    healthy
                        ? "Online — API and database healthy"
                        : $"Responding but unhealthy (HTTP {(int)response.StatusCode})");
            }
            catch (Exception ex)
            {
                SetLed(CloudLed, CloudStatusText, Red, $"Unreachable — {ShortError(ex)}");
            }

            // 2. Device heartbeat + last backup (needs the Agent's login).
            if (!healthy)
            {
                LastSeenText.Text = "—";
                LastBackupText.Text = "Server not reachable.";
            }
            else if (!_settings.HasCredentials)
            {
                LastSeenText.Text = "—";
                LastBackupText.Text =
                    "Agent login not configured (ErongoIT.Backup.Agent\\appsettings.Development.json).";
            }
            else
            {
                await RefreshDeviceAndJobsAsync();
            }

            FooterText.Text = $"Last checked {DateTime.Now:HH:mm:ss} • {_settings.ApiBaseUrl}";
        }
        finally
        {
            _cloudRefreshing = false;
        }
    }

    private async Task RefreshDeviceAndJobsAsync()
    {
        try
        {
            var device = await GetJsonAsync($"api/devices/{_settings.DeviceId}");

            if (device is { } d &&
                d.TryGetProperty("lastSeenAtUtc", out var seen) &&
                seen.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(seen.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var seenUtc))
            {
                var ago = DateTime.UtcNow - seenUtc;
                LastSeenText.Text =
                    $"{seenUtc.ToLocalTime():dd MMM HH:mm} ({FormatAgo(ago)})" +
                    (ago > TimeSpan.FromMinutes(5) ? " — Agent not reporting in!" : string.Empty);
                LastSeenText.Foreground = ago > TimeSpan.FromMinutes(5) ? Red : Brushes.Black;
            }
            else
            {
                LastSeenText.Text = "Never";
                LastSeenText.Foreground = Brushes.Black;
            }

            var jobs = await GetJsonAsync($"api/devices/{_settings.DeviceId}/backup-jobs");

            if (jobs is not { ValueKind: JsonValueKind.Array } list || list.GetArrayLength() == 0)
            {
                LastBackupText.Text = "No backups yet.";
                LastBackupText.Foreground = Brushes.Black;
                return;
            }

            var latest = list.EnumerateArray()
                .OrderByDescending(j => j.TryGetProperty("startedAtUtc", out var s) ? s.GetString() : "")
                .First();

            var status = latest.TryGetProperty("status", out var st) ? st.GetString() ?? "?" : "?";
            var started = ParseUtc(latest, "startedAtUtc");
            var uploaded = latest.TryGetProperty("bytesUploaded", out var bu) ? bu.GetInt64() : 0;
            var error = latest.TryGetProperty("errorMessage", out var em) && em.ValueKind == JsonValueKind.String
                ? em.GetString()
                : null;

            var when = started is null ? "?" : $"{started.Value.ToLocalTime():dd MMM HH:mm}";

            LastBackupText.Text = status switch
            {
                "Completed" => $"{when} • Completed • {FormatBytes(uploaded)} uploaded",
                "Running" => $"{when} • Running now...",
                "Failed" => $"{when} • FAILED — {error}",
                _ => $"{when} • {status}"
            };

            LastBackupText.Foreground = status switch
            {
                "Completed" => Brushes.ForestGreen,
                "Failed" => Red,
                _ => Brushes.Black
            };
        }
        catch (Exception ex)
        {
            LastBackupText.Text = $"Could not read backup status — {ShortError(ex)}";
            LastBackupText.Foreground = Amber;
        }
    }

    private async Task<JsonElement?> GetJsonAsync(string relativeUrl)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var token = await GetTokenAsync();

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{_settings.ApiBaseUrl}/{relativeUrl}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _http.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && attempt == 0)
            {
                _accessToken = null;
                continue;
            }

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;

            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.Clone();
        }

        return null;
    }

    private async Task<string> GetTokenAsync()
    {
        if (_accessToken is not null && DateTime.UtcNow < _accessTokenExpiresUtc)
            return _accessToken;

        using var response = await _http.PostAsJsonAsync(
            $"{_settings.ApiBaseUrl}/api/auth/login",
            new { username = _settings.Username, password = _settings.Password });

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"login failed (HTTP {(int)response.StatusCode})");

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        _accessToken = document.RootElement.GetProperty("accessToken").GetString()
                       ?? throw new InvalidOperationException("empty access token");

        // Tokens last 8 hours; renew well before that.
        _accessTokenExpiresUtc = DateTime.UtcNow.AddHours(6);

        return _accessToken;
    }

    /// <summary>
    /// The Agent logs "Next run=..." every time it checks its schedule.
    /// Read the most recent one from the end of the log.
    /// </summary>
    private string? ReadNextRunFromAgentLog()
    {
        var path = System.IO.Path.Combine(_logPath, "Agent.log");

        if (!File.Exists(path))
            return null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            const int tailBytes = 64 * 1024;
            if (stream.Length > tailBytes)
                stream.Seek(-tailBytes, SeekOrigin.End);

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var tail = reader.ReadToEnd();

            var matches = Regex.Matches(tail, @"Next run=([^\r\n]+?)\.?\s*$", RegexOptions.Multiline);

            if (matches.Count == 0)
                return null;

            var text = matches[^1].Groups[1].Value.Trim();

            return DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out var next) ||
                   DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out next)
                ? $"{next:ddd dd MMM HH:mm}"
                : text;
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------
    // Buttons
    // ------------------------------------------------------------------

    private void OpenPortal_Click(object sender, RoutedEventArgs e) =>
        OpenWithShell(_settings.ApiBaseUrl);

    private void ViewAgentLog_Click(object sender, RoutedEventArgs e)
    {
        var path = System.IO.Path.Combine(_logPath, "Agent.log");

        if (!File.Exists(path))
        {
            MessageBox.Show(this, "No Agent log yet. Start the Agent first.", "Agent Log",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("Unable to open log", ex);
        }
    }

    private async void AgentStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (IsProjectRunning("ErongoIT.Backup.Agent"))
            {
                RefreshLocalStatus();
                return;
            }

            StartProjectIndependently("Agent", "ErongoIT.Backup.Agent");

            AgentStatusText.Text = "Starting...";
            await Task.Delay(1500);
            RefreshLocalStatus();
        }
        catch (Exception ex)
        {
            ShowError("Unable to start Agent", ex);
        }
    }

    private void AgentStop_Click(object sender, RoutedEventArgs e)
    {
        StopProjectProcesses("ErongoIT.Backup.Agent");
        RefreshLocalStatus();
    }

    private void LaunchVpsGui_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartProjectIndependently(
                "VpsAgentGui",
                "ErongoIT.Backup.Agent.Gui",
                $"--api-base-url {_settings.ApiBaseUrl}");
        }
        catch (Exception ex)
        {
            ShowError("Unable to open Backup GUI", ex);
        }
    }

    private void LaunchGui_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            StartProjectIndependently("AgentGui", "ErongoIT.Backup.Agent.Gui", "--profile local");
        }
        catch (Exception ex)
        {
            ShowError("Unable to launch local GUI", ex);
        }
    }

    private async void ApiStart_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApiStatusText.Text = "Starting...";
            await RunDockerComposeAsync("up -d api");
            await Task.Delay(500);
            RefreshLocalStatus();
        }
        catch (Exception ex)
        {
            ShowError("Unable to start local API", ex);
            RefreshLocalStatus();
        }
    }

    private async void ApiStop_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            ApiStatusText.Text = "Stopping...";
            await RunDockerComposeAsync("stop api");
            await Task.Delay(500);
            RefreshLocalStatus();
        }
        catch (Exception ex)
        {
            ShowError("Unable to stop local API", ex);
            RefreshLocalStatus();
        }
    }

    // ------------------------------------------------------------------
    // Process helpers
    // ------------------------------------------------------------------

    private void StartProjectIndependently(string name, string project, string? additionalArguments = null)
    {
        var projectPath = System.IO.Path.Combine(_rootPath, project, project + ".csproj");

        if (!File.Exists(projectPath))
            throw new FileNotFoundException($"Project was not found:\n\n{projectPath}");

        var logFile = System.IO.Path.Combine(_logPath, $"{name}.log");

        // "cmd /c start" creates a process independent of the Service
        // Manager, so closing this window does not stop the Agent.
        var command =
            $"dotnet run --project \"{projectPath}\"" +
            (string.IsNullOrWhiteSpace(additionalArguments) ? string.Empty : $" -- {additionalArguments}") +
            $" >> \"{logFile}\" 2>&1";

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c start \"{name}\" /min cmd.exe /c \"{command}\"",
            WorkingDirectory = _rootPath,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var launcher = Process.Start(psi)
                             ?? throw new InvalidOperationException($"Unable to start {name}.");
    }

    private async Task RunDockerComposeAsync(string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            Arguments = $"compose {arguments}",
            WorkingDirectory = _rootPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = new Process { StartInfo = psi };

        if (!process.Start())
            throw new InvalidOperationException("Unable to start Docker Compose.");

        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        await process.WaitForExitAsync();

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output : error);
    }

    private static bool IsProjectRunning(string projectName)
    {
        try
        {
            return Process.GetProcesses().Any(process =>
            {
                try
                {
                    return !process.HasExited &&
                           process.ProcessName.Equals(projectName, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
                finally
                {
                    process.Dispose();
                }
            });
        }
        catch
        {
            return false;
        }
    }

    private static void StopProjectProcesses(string projectName)
    {
        Process[] processes;

        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return;
        }

        foreach (var process in processes)
        {
            try
            {
                if (process.HasExited ||
                    !process.ProcessName.Equals(projectName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }
    }

    private static bool IsPortOpen(int port)
    {
        try
        {
            using var client = new TcpClient();
            return client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(300));
        }
        catch
        {
            return false;
        }
    }

    // ------------------------------------------------------------------
    // UI helpers
    // ------------------------------------------------------------------

    private static void SetLed(Ellipse led, System.Windows.Controls.TextBlock text, Brush colour, string message)
    {
        led.Fill = colour;
        text.Text = message;
        text.Foreground = colour == Grey ? Brushes.DimGray : colour;
    }

    private static DateTime? ParseUtc(JsonElement element, string name)
    {
        if (element.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string FormatAgo(TimeSpan ago)
    {
        if (ago < TimeSpan.FromMinutes(1)) return "just now";
        if (ago < TimeSpan.FromHours(1)) return $"{(int)ago.TotalMinutes} min ago";
        if (ago < TimeSpan.FromDays(1)) return $"{(int)ago.TotalHours} h ago";
        return $"{(int)ago.TotalDays} days ago";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double value = bytes;
        var unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return unit == 0 ? $"{bytes} B" : $"{value:0.#} {units[unit]}";
    }

    private static string ShortError(Exception ex) =>
        ex is TaskCanceledException ? "timed out" : ex.GetBaseException().Message;

    private static void OpenWithShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch
        {
        }
    }

    private void ShowError(string title, Exception ex) =>
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
