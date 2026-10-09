using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;

namespace ErongoIT.Backup.Agent.Gui;

public partial class MainWindow : Window
{
    private readonly GuiOptions _options;
    private readonly BackupGuiApiClient _api;

    private readonly ObservableCollection<string> _folders = new();

    private DeviceDto? _device;
    private BackupPlanDto? _plan;
    private IReadOnlyList<BackupJobDto> _jobs =
        Array.Empty<BackupJobDto>();


    private CancellationTokenSource? _backupCancellation;
    private bool _backupRunning;

    public MainWindow()
    {
        InitializeComponent();

        _options = LoadOptions();

        _api = new BackupGuiApiClient(
            _options.ApiBaseUrl);

        FolderList.ItemsSource = _folders;

        var sourceFolders = _options.GetSourceFolders();

        SourcePathText.Text =
            sourceFolders.Count == 0
                ? "Not configured"
                : string.Join(Environment.NewLine, sourceFolders);

        SettingsApiUrl.Text = _options.ApiBaseUrl;
        SettingsCustomerId.Text = _options.CustomerId.ToString();
        SettingsDeviceId.Text = _options.DeviceId.ToString();
        SettingsModeText.Text = _options.IsEnrolled
            ? "Registered PC (device key in C:\\ProgramData\\ErongoIT Backup\\agent.json)"
            : "Developer profile (admin login)";
        FooterVersion.Text = "Agent " + AppVersion;

        WhatsNewText.Text = LoadChangelog();

        // Show every configured folder, including ones that no longer
        // exist, so they can still be removed.
        foreach (var folder in sourceFolders)
            _folders.Add(folder);

        UpdateFolderCount();

        InitializeTray();

        InitializePaging();

        InitializeAutoRefresh();

        InitializeUpdates();

        _ = LoadDataAsync();
    }

    /// <summary>Version set by the installer build (-p:Version=x.y.z).</summary>
    private static string AppVersion
    {
        get
        {
            var version = typeof(MainWindow).Assembly.GetName().Version;
            return version is null ? "?" : $"{version.Major}.{version.Minor}.{version.Build}";
        }
    }

    private static string LoadChangelog()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");

            if (!File.Exists(path))
                return "No release notes found.";

            // Plain-text view of the Markdown: drop heading marks.
            return string.Join(
                Environment.NewLine,
                File.ReadAllLines(path)
                    .Select(line => line.StartsWith("# ")
                        ? line[2..].ToUpperInvariant()
                        : line.StartsWith("## ")
                            ? Environment.NewLine + line[3..]
                            : line))
                .Trim();
        }
        catch (Exception ex)
        {
            return $"Release notes could not be loaded: {ex.Message}";
        }
    }

    private static GuiOptions LoadOptions()
    {
        var options = new GuiOptions();

        var apiBaseUrlOverride =
            GetCommandLineValue("--api-base-url");

        // Profile selection:
        //   --profile vps / --profile local  (explicit), or
        //   --api-base-url <non-localhost>  (implies vps)
        var explicitProfile =
            GetCommandLineValue("--profile");

        var profile = explicitProfile;

        if (string.IsNullOrWhiteSpace(profile))
        {
            profile =
                !string.IsNullOrWhiteSpace(apiBaseUrlOverride) &&
                !apiBaseUrlOverride.Contains(
                    "localhost",
                    StringComparison.OrdinalIgnoreCase) &&
                !apiBaseUrlOverride.Contains(
                    "127.0.0.1",
                    StringComparison.Ordinal)
                    ? "vps"
                    : "local";
        }

        profile = profile.Trim().ToLowerInvariant();

        // Later files override earlier ones.
        // agentgui.*.json use their own names so they never clash with
        // the appsettings files copied in from the referenced Agent project.
        var paths = new[]
        {
            Path.Combine(
                AppContext.BaseDirectory,
                "appsettings.json"),

            Path.Combine(
                AppContext.BaseDirectory,
                "appsettings.Development.json"),

            Path.Combine(
                AppContext.BaseDirectory,
                $"agentgui.{profile}.json")
        };

        foreach (var path in paths)
        {
            if (!File.Exists(path))
                continue;

            try
            {
                using var document =
                    JsonDocument.Parse(
                        File.ReadAllText(path));

                if (!document.RootElement.TryGetProperty(
                        "Agent",
                        out var agent))
                {
                    continue;
                }

                if (TryGetString(agent, "ApiBaseUrl", out var apiBaseUrl))
                    options.ApiBaseUrl = apiBaseUrl;

                // Accept both GUI names (Username/Password) and the
                // background Agent names (ApiUsername/ApiPassword).
                if (TryGetString(agent, "Username", out var username) ||
                    TryGetString(agent, "ApiUsername", out username))
                {
                    options.Username = username;
                }

                if (TryGetString(agent, "Password", out var password) ||
                    TryGetString(agent, "ApiPassword", out password))
                {
                    options.Password = password;
                }

                if (TryGetString(agent, "CustomerId", out var customerId) &&
                    Guid.TryParse(customerId, out var parsedCustomerId))
                {
                    options.CustomerId = parsedCustomerId;
                }

                if (TryGetString(agent, "DeviceId", out var deviceId) &&
                    Guid.TryParse(deviceId, out var parsedDeviceId))
                {
                    options.DeviceId = parsedDeviceId;
                }

                if (TryGetString(agent, "SourcePath", out var sourcePath))
                    options.SourcePath = sourcePath;
            }
            catch
            {
                // Continue with values already loaded.
            }
        }

        // A registered PC (agent.json from setup) always uses its own
        // server, device and key. Only an explicit "--profile local"
        // (developer testing against the local API) bypasses it.
        if (!string.Equals(explicitProfile?.Trim(), "local", StringComparison.OrdinalIgnoreCase))
        {
            var enrolled =
                ErongoIT.Backup.Agent.Configuration.AgentConfigFile.TryLoad();

            if (enrolled is not null)
            {
                try
                {
                    options.DeviceKey = enrolled.GetDeviceKey();
                }
                catch
                {
                    options.DeviceKey = string.Empty;
                }

                if (options.IsEnrolled)
                {
                    options.ApiBaseUrl = enrolled.ApiBaseUrl;
                    options.CustomerId = enrolled.CustomerId;
                    options.DeviceId = enrolled.DeviceId;
                    options.SourcePaths = new List<string>(enrolled.SourcePaths);
                    options.SourcePath = string.Empty;
                    options.Username = string.Empty;
                    options.Password = string.Empty;

                    return options;
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(apiBaseUrlOverride))
        {
            options.ApiBaseUrl =
                apiBaseUrlOverride.TrimEnd('/');
        }

        return options;
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;

        if (!element.TryGetProperty(propertyName, out var property) ||
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

    private static string? GetCommandLineValue(
        string argumentName)
    {
        var arguments =
            Environment.GetCommandLineArgs();

        for (var index = 0;
             index < arguments.Length - 1;
             index++)
        {
            if (string.Equals(
                    arguments[index],
                    argumentName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return arguments[index + 1];
            }
        }

        return null;
    }

    private async Task LoadDataAsync()
    {
        try
        {
            SetConnected(false, "Connecting...");

            SetConnected(false, "Authenticating...");

            if (_options.IsEnrolled)
            {
                await _api.DeviceLoginAsync(
                    _options.DeviceId,
                    _options.DeviceKey);
            }
            else
            {
                await _api.LoginAsync(
                    _options.Username,
                    _options.Password);
            }

            SetConnected(false, "Loading device...");

            _device = await _api.GetDeviceAsync(
                _options.DeviceId);

            if (_device is null)
            {
                SetConnected(false, "Device not found");

                ProtectionStatus.Text =
                    "Device not found";

                ProtectionDetails.Text =
                    "The configured device does not exist in the Backup API.";

                StatusIcon.Text = "!";
                return;
            }

            SetConnected(true, "Connected");

            DeviceName.Text =
                string.IsNullOrWhiteSpace(_device.Name)
                    ? _device.Hostname ?? "Unknown device"
                    : _device.Name;

            DeviceDetails.Text =
                $"{_device.OperatingSystem ?? "Unknown OS"}" +
                $" • {_device.Hostname ?? "No hostname"}";

            if (_device.CustomerId != _options.CustomerId)
            {
                ProtectionStatus.Text =
                    "Configuration error";

                ProtectionDetails.Text =
                    "The device belongs to a different customer.";

                StatusIcon.Text = "!";
                return;
            }

            if (!_device.IsActive)
            {
                ProtectionStatus.Text =
                    "Protection disabled";

                ProtectionDetails.Text =
                    "This device is inactive.";

                StatusIcon.Text = "!";
                return;
            }

            if (!_device.AssignedBackupPlanId.HasValue ||
                _device.AssignedBackupPlanId.Value == Guid.Empty)
            {
                ProtectionStatus.Text =
                    "Not protected";

                ProtectionDetails.Text =
                    "No backup plan is assigned to this device.";

                PlanName.Text = "No plan assigned";
                PlanDetails.Text = "No backup plan";
                BackupPagePlan.Text = "No plan assigned";
                BackupPageSchedule.Text = "—";
                BackupPageRetention.Text = "—";

                StatusIcon.Text = "!";
                return;
            }

            var plans =
                await _api.GetBackupPlansAsync(
                    _options.CustomerId);

            _plan = plans.FirstOrDefault(
                x => x.Id ==
                     _device.AssignedBackupPlanId.Value);

            if (_plan is null)
            {
                ProtectionStatus.Text =
                    "Plan not found";

                ProtectionDetails.Text =
                    "The assigned backup plan could not be loaded.";

                StatusIcon.Text = "!";
                return;
            }

            PlanName.Text = _plan.Name;

            PlanDetails.Text =
                $"{GetScheduleDescription(_plan)} • " +
                $"Retention {_plan.RetentionDays} days";

            BackupPagePlan.Text = _plan.Name;
            BackupPageSchedule.Text =
                GetScheduleDescription(_plan);
            BackupPageRetention.Text =
                $"{_plan.RetentionDays} days";

            if (!_plan.IsEnabled)
            {
                ProtectionStatus.Text =
                    "Protection paused";

                ProtectionDetails.Text =
                    $"Backup plan \"{_plan.Name}\" is disabled.";

                StatusIcon.Text = "!";
            }
            else
            {
                ProtectionStatus.Text =
                    "Your computer is protected";

                ProtectionDetails.Text =
                    $"Backup plan \"{_plan.Name}\" is active.";

                StatusIcon.Text = "✓";
            }

            _jobs =
                await _api.GetBackupJobsAsync(
                    _options.DeviceId);

            UpdateLatestBackup();
            UpdateCurrentJob();
        }
        catch (Exception ex)
        {
            SetConnected(
                false,
                "Connection error");

            ProtectionStatus.Text =
                "Unable to connect";

            ProtectionDetails.Text =
                ex.Message;

            StatusIcon.Text = "!";
        }
    }

    private async void BackUpNow_Click(
        object sender,
        RoutedEventArgs e)
    {
        await StartManualBackupAsync();
    }

    private async Task StartManualBackupAsync()
    {
        if (_backupRunning)
            return;

        if (_device is null ||
            _plan is null)
        {
            MessageBox.Show(
                this,
                "The device or backup plan has not finished loading.",
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (!_device.IsActive)
        {
            MessageBox.Show(
                this,
                "This device is inactive.",
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (!_plan.IsEnabled)
        {
            MessageBox.Show(
                this,
                "The assigned backup plan is disabled.",
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var folders =
            _folders
                .Where(Directory.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        if (folders.Count == 0)
        {
            MessageBox.Show(
                this,
                "No backup folders are selected.",
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var existingActiveJob =
            _jobs.FirstOrDefault(
                x =>
                    x.BackupPlanId == _plan.Id &&
                    (x.Status.Equals(
                         "Pending",
                         StringComparison.OrdinalIgnoreCase) ||
                     x.Status.Equals(
                         "Running",
                         StringComparison.OrdinalIgnoreCase)));

        if (existingActiveJob is not null)
        {
            MessageBox.Show(
                this,
                $"Backup job {existingActiveJob.Id} is already running.",
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        // Registered PCs: the background service runs the backup (speed
        // limits, file history, keeps running when this window closes).
        // The window only asks for it.
        if (_options.IsEnrolled)
        {
            await RequestServiceBackupAsync();
            return;
        }

        _backupCancellation =
            new CancellationTokenSource();

        _backupRunning = true;

        SetBackupButtons(true);

        try
        {
            await RunBackupAsync(
                folders,
                _backupCancellation.Token);

            await RefreshJobsAsync();

            ProtectionStatus.Text =
                "Your files are protected";

            ProtectionDetails.Text =
                "The latest backup completed successfully.";

            StatusIcon.Text = "✓";

            MessageBox.Show(
                this,
                "Backup completed successfully.",
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (OperationCanceledException)
        {
            ProtectionStatus.Text =
                "Backup stopped";

            ProtectionDetails.Text =
                "The backup was stopped by the user.";

            StatusIcon.Text = "!";

            await RefreshJobsAsync();

            MessageBox.Show(
                this,
                "The backup was stopped.",
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ProtectionStatus.Text =
                "Backup failed";

            ProtectionDetails.Text =
                ex.Message;

            StatusIcon.Text = "!";

            await RefreshJobsAsync();

            MessageBox.Show(
                this,
                ex.Message,
                "Backup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _backupCancellation?.Dispose();
            _backupCancellation = null;

            _backupRunning = false;

            SetBackupButtons(false);
        }
    }

    private async Task RunBackupAsync(
        IReadOnlyList<string> folders,
        CancellationToken cancellationToken)
    {
        if (_plan is null)
            throw new InvalidOperationException(
                "No backup plan is loaded.");

        BackupProgress.Value = 0;
        BackupProgressPercent.Text = "0%";
        BackupProgressText.Text =
            "Preparing backup...";
        CurrentJobStatus.Text = "Starting";

        var files = new List<BackupFile>();

        foreach (var folder in folders)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!Directory.Exists(folder))
                continue;

            foreach (var filePath in Directory.EnumerateFiles(
                         folder,
                         "*",
                         SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileInfo =
                    new FileInfo(filePath);

                if (!fileInfo.Exists)
                    continue;

                var relativePath =
                    Path.GetRelativePath(
                        folder,
                        filePath);

                var folderName =
                    new DirectoryInfo(folder).Name;

                var backupPath =
                    Path.Combine(
                        folderName,
                        relativePath);

                files.Add(
                    new BackupFile(
                        filePath,
                        backupPath,
                        fileInfo.Length));
            }
        }

        if (files.Count == 0)
            throw new InvalidOperationException(
                "No files were found in the selected folders.");

        long bytesSelected =
            files.Sum(x => x.Length);

        long bytesProcessed = 0;
        long bytesUploaded = 0;
        long bytesTransferred = 0;
        var filesUploaded = 0;
        var filesSkipped = 0;
        var filesChecked = 0;

        BackupProgressText.Text =
            $"Found {files.Count:N0} file(s).";

        var job =
            await _api.CreateBackupJobAsync(
                _options.CustomerId,
                _options.DeviceId,
                _plan.Id,
                type: 1,
                cancellationToken);

        CurrentJobStatus.Text = "Starting";

        await _api.StartBackupJobAsync(
            job.Id,
            cancellationToken);

        CurrentJobStatus.Text = "Running";

        void ShowProgress(
            long currentFileBytes,
            string message)
        {
            var done = bytesProcessed + currentFileBytes;

            var percent =
                bytesSelected > 0
                    ? done * 100.0 / bytesSelected
                    : 0;

            percent = Math.Clamp(percent, 0, 100);

            BackupProgress.Value = percent;
            BackupProgressPercent.Text = $"{percent:0}%";
            BackupProgressText.Text = message;
        }

        try
        {
            const int batchSize = 200;

            // Reuse hashes of files whose size and modified time are unchanged.
            var hashCache =
                ErongoIT.Backup.Agent.Backup.FileHashCache.Load("gui");

            for (var index = 0; index < files.Count; index += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = files
                    .Skip(index)
                    .Take(batchSize)
                    .ToList();

                // 1. Hash locally (off the UI thread).
                var requests =
                    new List<ErongoIT.Backup.Agent.Backup.ExistingFileRequest>();

                foreach (var file in batch)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    filesChecked++;

                    ShowProgress(
                        0,
                        $"Checking {filesChecked:N0} of {files.Count:N0}: {file.RelativePath}");

                    try
                    {
                        var sha256 = await Task.Run(
                            () => hashCache.GetSha256Async(
                                file.FullPath,
                                cancellationToken),
                            cancellationToken);

                        requests.Add(
                            new ErongoIT.Backup.Agent.Backup.ExistingFileRequest(
                                file.RelativePath,
                                sha256,
                                file.Length));
                    }
                    catch (Exception ex) when (
                        ex is IOException or UnauthorizedAccessException)
                    {
                        // Locked or unreadable file: skip it, keep going.
                        bytesSelected -= file.Length;
                    }
                }

                // 2. Ask the server which files it already has.
                var result =
                    await _api.RegisterExistingFilesAsync(
                        job.Id,
                        _options.CustomerId,
                        _options.DeviceId,
                        requests,
                        cancellationToken);

                var missing =
                    result.MissingRelativePaths
                        .ToHashSet(StringComparer.Ordinal);

                filesSkipped += result.RegisteredCount;
                bytesProcessed += result.RegisteredBytes;

                ShowProgress(
                    0,
                    $"{filesSkipped:N0} unchanged file(s) already backed up");

                // 3. Upload only new or changed files, with live progress.
                foreach (var file in batch)
                {
                    if (!missing.Contains(file.RelativePath))
                        continue;

                    cancellationToken.ThrowIfCancellationRequested();

                    var fileLength = file.Length;
                    var relativePath = file.RelativePath;

                    var progress = new Progress<long>(sent =>
                    {
                        var filePercent =
                            fileLength > 0
                                ? sent * 100.0 / fileLength
                                : 100;

                        ShowProgress(
                            Math.Min(sent, fileLength),
                            $"Uploading {relativePath} • " +
                            $"{FormatBytes(sent)} of {FormatBytes(fileLength)} " +
                            $"({filePercent:0}%)");
                    });

                    ShowProgress(
                        0,
                        $"Preparing {relativePath}");

                    await using var prepared =
                        await Task.Run(
                            () => ErongoIT.Backup.Agent.Backup.UploadCompression
                                .PrepareAsync(
                                    file.FullPath,
                                    cancellationToken),
                            cancellationToken);

                    // Progress is reported in original-file bytes, even when
                    // a smaller compressed copy is what travels.
                    var scale =
                        prepared.UploadLength > 0
                            ? (double)fileLength / prepared.UploadLength
                            : 1.0;

                    ShowProgress(
                        0,
                        prepared.IsCompressed
                            ? $"Uploading {relativePath} (compressed to {FormatBytes(prepared.UploadLength)})"
                            : $"Uploading {relativePath}");

                    await using (var stream =
                        new ErongoIT.Backup.Agent.Backup.ProgressReadStream(
                            ErongoIT.Backup.Agent.Backup.FileHashing
                                .OpenForUpload(prepared.UploadPath),
                            sent => ((IProgress<long>)progress).Report(
                                (long)(sent * scale))))
                    {
                        await _api.UploadFileAsync(
                            job.Id,
                            _options.CustomerId,
                            _options.DeviceId,
                            relativePath,
                            stream,
                            Path.GetFileName(file.FullPath),
                            cancellationToken,
                            prepared.Encoding);
                    }

                    bytesTransferred += prepared.UploadLength;
                    bytesUploaded += fileLength;
                    bytesProcessed += fileLength;
                    filesUploaded++;

                    ShowProgress(
                        0,
                        $"{filesUploaded:N0} uploaded • {filesSkipped:N0} unchanged • " +
                        $"{FormatBytes(bytesProcessed)} of {FormatBytes(bytesSelected)}");
                }
            }

            bytesSelected = Math.Max(bytesSelected, 0);
            bytesUploaded = Math.Min(bytesUploaded, bytesSelected);

            hashCache.Save();

            await _api.CompleteBackupJobAsync(
                job.Id,
                bytesSelected,
                bytesUploaded,
                cancellationToken);

            BackupProgress.Value = 100;
            BackupProgressPercent.Text = "100%";

            BackupProgressText.Text =
                $"{filesUploaded:N0} uploaded ({FormatBytes(bytesUploaded)}, " +
                $"sent {FormatBytes(bytesTransferred)}) • " +
                $"{filesSkipped:N0} unchanged";

            CurrentJobStatus.Text = "Completed";
        }
        catch (Exception ex)
        {
            try
            {
                await _api.FailBackupJobAsync(
                    job.Id,
                    ex.Message,
                    CancellationToken.None);
            }
            catch
            {
                // The original failure is more important.
            }

            CurrentJobStatus.Text = "Failed";
            throw;
        }
    }

    private async Task RequestServiceBackupAsync()
    {
        try
        {
            await _api.RequestBackupAsync(_options.DeviceId);

            ProtectionDetails.Text =
                "Backup requested. The backup service starts it within 30 seconds.";

            MessageBox.Show(
                this,
                "Backup requested.\n\nThe backup service starts it within 30 seconds. " +
                "Progress is shown on the Overview and History pages.",
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            _ = AutoRefreshAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"The backup could not be requested.\n\n{ex.Message}",
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void ChangeSetup_Click(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Environment.ProcessPath!,
                    Arguments = "--setup",
                    UseShellExecute = true,
                    Verb = "runas"
                });

            ExitApplication();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the administrator prompt.
        }
    }

    private void StopBackup_Click(
        object sender,
        RoutedEventArgs e)
    {
        _backupCancellation?.Cancel();
    }

    private void SetBackupButtons(
        bool running)
    {
        BackUpNowButton.IsEnabled = !running;
        BackupPageBackUpNowButton.IsEnabled = !running;

        StopBackupButton.Visibility =
            running
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async Task RefreshJobsAsync()
    {
        try
        {
            _jobs =
                await _api.GetBackupJobsAsync(
                    _options.DeviceId);

            UpdateLatestBackup();
            UpdateCurrentJob();
        }
        catch
        {
            // Do not hide the original backup result.
        }
    }

    private void UpdateLatestBackup()
    {
        var latest =
            _jobs
                .Where(x =>
                    x.Status.Equals(
                        "Completed",
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(
                    x => x.CompletedAtUtc)
                .FirstOrDefault();

        if (latest is null)
        {
            LatestBackupText.Text =
                "No completed backups found.";

            return;
        }

        LatestBackupText.Text =
            $"{latest.CompletedAtUtc?.ToLocalTime():dd MMM yyyy HH:mm} " +
            $"• {FormatBytes(latest.BytesUploaded)} uploaded";
    }

    private void UpdateCurrentJob()
    {
        var active =
            _jobs.FirstOrDefault(
                x =>
                    x.Status.Equals(
                        "Running",
                        StringComparison.OrdinalIgnoreCase) ||
                    x.Status.Equals(
                        "Pending",
                        StringComparison.OrdinalIgnoreCase));

        if (active is null)
        {
            if (!_backupRunning)
            {
                CurrentJobStatus.Text = "Idle";
                BackupProgress.Value = 0;
                BackupProgressPercent.Text = "0%";
                BackupProgressText.Text =
                    "No active backup";
            }

            return;
        }

        CurrentJobStatus.Text = active.Status;

        var percent =
            active.BytesSelected > 0
                ? active.BytesUploaded * 100.0 /
                  active.BytesSelected
                : 0;

        BackupProgress.Value =
            Math.Clamp(
                percent,
                0,
                100);

        BackupProgressPercent.Text =
            $"{percent:0}%";

        BackupProgressText.Text =
            $"{FormatBytes(active.BytesUploaded)} of " +
            $"{FormatBytes(active.BytesSelected)}";
    }

    private void SetConnected(
        bool connected,
        string text)
    {
        ConnectionText.Text = text;

        ConnectionIndicator.Fill =
            connected
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Gray;
    }

    private static string GetScheduleDescription(
        BackupPlanDto plan)
    {
        var time =
            TimeSpan
                .FromMinutes(
                    Math.Clamp(
                        plan.ScheduleTimeMinutes,
                        0,
                        1439))
                .ToString(@"hh\:mm");

        return plan.ScheduleType switch
        {
            1 =>
                $"Continuous • every {Math.Max(15, plan.IntervalMinutes)} minutes",

            2 =>
                $"Daily at {time}",

            3 =>
                $"Weekly on {GetDayName(plan.ScheduleDayOfWeek)} at {time}",

            _ =>
                "Unknown schedule"
        };
    }

    private static string GetDayName(
        int day)
    {
        return day switch
        {
            0 => "Sunday",
            1 => "Monday",
            2 => "Tuesday",
            3 => "Wednesday",
            4 => "Thursday",
            5 => "Friday",
            6 => "Saturday",
            _ => "Unknown day"
        };
    }

    private static string FormatBytes(
        long bytes)
    {
        if (bytes < 1024)
            return $"{bytes} B";

        if (bytes < 1024 * 1024)
            return $"{bytes / 1024.0:0.0} KB";

        if (bytes < 1024L * 1024L * 1024L)
            return $"{bytes / (1024.0 * 1024.0):0.0} MB";

        return
            $"{bytes / (1024.0 * 1024.0 * 1024.0):0.0} GB";
    }

    private void ShowView(
        FrameworkElement view)
    {
        OverviewView.Visibility = Visibility.Collapsed;
        BackupView.Visibility = Visibility.Collapsed;
        RestoreView.Visibility = Visibility.Collapsed;
        HistoryView.Visibility = Visibility.Collapsed;
        SettingsView.Visibility = Visibility.Collapsed;

        view.Visibility = Visibility.Visible;
    }

    private void Overview_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowView(OverviewView);
    }

    private void Backup_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowView(BackupView);
    }

    private void History_Click(
        object sender,
        RoutedEventArgs e)
    {
        SetHistoryRows(
            _jobs
                .OrderByDescending(
                    x => x.StartedAtUtc)
                .Select(
                    x => new HistoryRow(x))
                .ToList());

        ShowView(HistoryView);

        // Show the cached list at once, then fetch the latest from the server.
        _ = AutoRefreshAsync();
    }


    private void Settings_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowView(SettingsView);
    }

    private void AddFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new Microsoft.Win32.OpenFolderDialog
            {
                Title =
                    "Select a folder to include in the backup.",
                Multiselect = false
            };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var selectedPath = dialog.FolderName;

        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return;
        }

        if (_folders.Contains(
                selectedPath,
                StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var updated = _folders.ToList();
        updated.Add(selectedPath);

        SaveFolders(updated);
    }

    private void RemoveFolder_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string folder })
        {
            return;
        }

        if (_folders.Count <= 1)
        {
            MessageBox.Show(
                this,
                "At least one folder must stay in the backup.\n\n" +
                "Add the new folder first, then remove this one.",
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Stop backing up this folder?\n\n{folder}\n\n" +
            "Nothing is deleted from this PC. Copies already on the backup " +
            "server stay restorable until the plan's retention period removes them.",
            "Remove folder",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        var updated = _folders
            .Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))
            .ToList();

        SaveFolders(updated);
    }

    /// <summary>
    /// Saves the new folder list (registered PCs: into agent.json for the
    /// backup service) and refreshes the screen. Nothing changes on screen
    /// if saving fails or the admin prompt is declined.
    /// </summary>
    private void SaveFolders(
        List<string> updated)
    {
        if (_options.IsEnrolled)
        {
            var result = FolderSettings.Save(
                updated,
                out var error);

            if (result == FolderSettings.SaveResult.Cancelled)
            {
                return;
            }

            if (result == FolderSettings.SaveResult.Failed)
            {
                MessageBox.Show(
                    this,
                    $"The folder list could not be saved.\n\n{error}",
                    "ErongoIT Backup",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }
        }

        _folders.Clear();

        foreach (var folder in updated)
            _folders.Add(folder);

        _options.SourcePaths = new List<string>(updated);
        _options.SourcePath = string.Empty;

        SourcePathText.Text =
            updated.Count == 0
                ? "Not configured"
                : string.Join(Environment.NewLine, updated);

        UpdateFolderCount();
    }

    private void UpdateFolderCount()
    {
        FolderCountText.Text =
            _folders.Count == 1
                ? "1 folder selected"
                : $"{_folders.Count} folders selected";
    }

    private static string FormatDate(
        DateTime? value)
    {
        return value.HasValue
            ? value.Value
                .ToLocalTime()
                .ToString("dd MMM yyyy HH:mm:ss")
            : "—";
    }

    private sealed record BackupFile(
        string FullPath,
        string RelativePath,
        long Length);

    private sealed class HistoryRow
    {
        public Guid Id { get; }
        public string Status { get; }
        public string StartedDisplay { get; }
        public string CompletedDisplay { get; }
        public string FilesDisplay { get; }
        public string UploadedDisplay { get; }

        public HistoryRow(BackupJobDto job)
        {
            Id = job.Id;
            Status = job.Status;
            StartedDisplay =
                FormatDate(job.StartedAtUtc);
            CompletedDisplay =
                FormatDate(job.CompletedAtUtc);
            FilesDisplay = "—";
            UploadedDisplay =
                FormatBytes(job.BytesUploaded);
        }
    }
}
