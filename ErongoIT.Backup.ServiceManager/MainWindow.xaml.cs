using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ErongoIT.Backup.ServiceManager;

public partial class MainWindow : Window
{
    private readonly string _rootPath;
    private readonly string _logPath;

    private Process? _apiProcess;
    private Process? _agentProcess;
    private Process? _webProcess;
    private Process? _guiProcess;

    private readonly DispatcherTimer _statusTimer;

    public MainWindow()
    {
        InitializeComponent();

        _rootPath = FindRepositoryRoot();

        _logPath = Path.Combine(
            _rootPath,
            "logs");

        Directory.CreateDirectory(_logPath);

        _statusTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };

        _statusTimer.Tick += (_, _) =>
        {
            RefreshStatus();
        };

        Loaded += (_, _) =>
        {
            RefreshStatus();
            _statusTimer.Start();
        };

        Closing += (_, _) =>
        {
            _statusTimer.Stop();
        };
    }

    private static string FindRepositoryRoot()
    {
        var startingDirectories = new[]
        {
            Environment.CurrentDirectory,
            AppContext.BaseDirectory
        };

        foreach (var startingDirectory in startingDirectories)
        {
            var current =
                new DirectoryInfo(startingDirectory);

            while (current is not null)
            {
                var apiProject =
                    Path.Combine(
                        current.FullName,
                        "ErongoIT.Backup.Api",
                        "ErongoIT.Backup.Api.csproj");

                if (File.Exists(apiProject))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }
        }

        throw new InvalidOperationException(
            "Could not locate the ErongoIT.Backup repository root.");
    }

    private void RefreshStatus_Click(
        object sender,
        RoutedEventArgs e)
    {
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var apiRunning =
            IsProcessRunning(_apiProcess) ||
            IsPortOpen(5230);

        var agentRunning =
            IsProcessRunning(_agentProcess);

        var webRunning =
            IsProcessRunning(_webProcess) ||
            IsPortOpen(5165);

        SetStatus(
            ApiLed,
            ApiStatusText,
            apiRunning);

        SetStatus(
            AgentLed,
            AgentStatusText,
            agentRunning);

        SetStatus(
            WebLed,
            WebStatusText,
            webRunning);

        if (apiRunning &&
            agentRunning &&
            webRunning)
        {
            OverallStatusText.Text =
                "All services running.";
        }
        else if (!apiRunning &&
                 !agentRunning &&
                 !webRunning)
        {
            OverallStatusText.Text =
                "All services stopped.";
        }
        else
        {
            OverallStatusText.Text =
                "Service status updated.";
        }
    }

    private static void SetStatus(
        System.Windows.Shapes.Ellipse led,
        System.Windows.Controls.TextBlock text,
        bool running)
    {
        led.Fill =
            running
                ? Brushes.LimeGreen
                : Brushes.Red;

        text.Text =
            running
                ? "Running"
                : "Stopped";

        text.Foreground =
            running
                ? Brushes.ForestGreen
                : Brushes.Red;
    }

    private void ApiStart_Click(
        object sender,
        RoutedEventArgs e)
    {
        StartService(
            "API",
            "ErongoIT.Backup.Api",
            ref _apiProcess);
    }

    private void ApiStop_Click(
        object sender,
        RoutedEventArgs e)
    {
        StopService(ref _apiProcess);
        StopProcessOnPort(5230);
        RefreshStatus();
    }

    private void AgentStart_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsPortOpen(5230))
        {
            MessageBox.Show(
                this,
                "The Backup API must be running before the Agent can start.",
                "Backup API Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        StartService(
            "Agent",
            "ErongoIT.Backup.Agent",
            ref _agentProcess);
    }

    private void AgentStop_Click(
        object sender,
        RoutedEventArgs e)
    {
        StopService(
            ref _agentProcess);

        RefreshStatus();
    }

    private void WebStart_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (!IsPortOpen(5230))
        {
            MessageBox.Show(
                this,
                "The Backup API must be running before the Web application can start.",
                "Backup API Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        StartService(
            "Web",
            "ErongoIT.Backup.Web",
            ref _webProcess);
    }

    private void WebStop_Click(
        object sender,
        RoutedEventArgs e)
    {
        StopService(
            ref _webProcess);

        RefreshStatus();
    }

    private void LaunchGui_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            if (IsProcessRunning(_guiProcess))
            {
                MessageBox.Show(
                    this,
                    "The Agent GUI is already running.",
                    "Agent GUI",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                return;
            }

            _guiProcess =
                StartDotnetProject(
                    "AgentGui",
                    "ErongoIT.Backup.Agent.Gui");
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Unable to launch GUI",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StartService(
        string name,
        string project,
        ref Process? process)
    {
        try
        {
            if (IsProcessRunning(process))
                return;

            process =
                StartDotnetProject(
                    name,
                    project);

            RefreshStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                ex.Message,
                $"Unable to start {name}",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private Process StartDotnetProject(
        string name,
        string project)
    {
        var projectPath =
            Path.Combine(
                _rootPath,
                project,
                project + ".csproj");

        if (!File.Exists(projectPath))
        {
            throw new FileNotFoundException(
                $"Project was not found:\n\n{projectPath}");
        }

        var logFile =
            Path.Combine(
                _logPath,
                $"{name}.log");

        var psi =
            new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments =
                    $"run --project \"{projectPath}\"",
                WorkingDirectory = _rootPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

        var process =
            new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data is null)
                return;

            try
            {
                File.AppendAllText(
                    logFile,
                    args.Data +
                    Environment.NewLine);
            }
            catch
            {
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data is null)
                return;

            try
            {
                File.AppendAllText(
                    logFile,
                    "[ERROR] " +
                    args.Data +
                    Environment.NewLine);
            }
            catch
            {
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Unable to start {name}.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        return process;
    }

    private static void StopService(ref Process? process)
    {
        if (!IsProcessRunning(process))
        {
            process = null;
            return;
        }

        try
        {
            process!.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch
        {
        }
        finally
        {
            process.Dispose();
            process = null;
        }
    }

    private static void StopProcessOnPort(int port)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat.exe",
                Arguments = "-ano",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };

            using var netstat = Process.Start(psi);

            if (netstat is null)
                return;

            var output = netstat.StandardOutput.ReadToEnd();
            netstat.WaitForExit(3000);

            foreach (var line in output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(" ", StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length < 5 ||
                    !parts[0].Equals("TCP", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!parts[1].EndsWith(":" + port, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!int.TryParse(parts[^1], out var pid))
                    continue;

                if (pid <= 0 || pid == Environment.ProcessId)
                    continue;

                try
                {
                    using var target = Process.GetProcessById(pid);
                    target.Kill(entireProcessTree: true);
                    target.WaitForExit(5000);
                }
                catch
                {
                }

                break;
            }
        }
        catch
        {
        }
    }


    private static bool IsProcessRunning(
        Process? process)
    {
        if (process is null)
            return false;

        try
        {
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPortOpen(
        int port)
    {
        try
        {
            using var client =
                new TcpClient();

            var task =
                client.ConnectAsync(
                    "127.0.0.1",
                    port);

            return task.Wait(
                TimeSpan.FromMilliseconds(150));
        }
        catch
        {
            return false;
        }
    }
}
