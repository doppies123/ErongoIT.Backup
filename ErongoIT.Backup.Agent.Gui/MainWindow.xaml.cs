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
    private IReadOnlyList<BackupFileDto> _restoreFiles =
        Array.Empty<BackupFileDto>();


    private CancellationTokenSource? _backupCancellation;
    private bool _backupRunning;

    public MainWindow()
    {
        InitializeComponent();

        _options = LoadOptions();

        _api = new BackupGuiApiClient(
            _options.ApiBaseUrl);

        FolderList.ItemsSource = _folders;

        SourcePathText.Text =
            string.IsNullOrWhiteSpace(_options.SourcePath)
                ? "Not configured"
                : _options.SourcePath;

        SettingsApiUrl.Text = _options.ApiBaseUrl;
        SettingsCustomerId.Text = _options.CustomerId.ToString();
        SettingsDeviceId.Text = _options.DeviceId.ToString();
        FooterVersion.Text = "Agent 1.0.0";

        if (!string.IsNullOrWhiteSpace(_options.SourcePath) &&
            Directory.Exists(_options.SourcePath))
        {
            _folders.Add(_options.SourcePath);
        }

        UpdateFolderCount();

        _ = LoadDataAsync();
    }

    private static GuiOptions LoadOptions()
    {
        var options = new GuiOptions();

        var apiBaseUrlOverride =
            GetCommandLineValue("--api-base-url");

        // Profile selection:
        //   --profile vps / --profile local  (explicit), or
        //   --api-base-url <non-localhost>  (implies vps)
        var profile =
            GetCommandLineValue("--profile");

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

            await _api.LoginAsync(
                _options.Username,
                _options.Password);

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
                            () => ErongoIT.Backup.Agent.Backup.FileHashing
                                .ComputeSha256Async(
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
                        $"Uploading {relativePath}");

                    await using (var stream =
                        new ErongoIT.Backup.Agent.Backup.ProgressReadStream(
                            ErongoIT.Backup.Agent.Backup.FileHashing
                                .OpenForUpload(file.FullPath),
                            sent => ((IProgress<long>)progress).Report(sent)))
                    {
                        await _api.UploadFileAsync(
                            job.Id,
                            _options.CustomerId,
                            _options.DeviceId,
                            relativePath,
                            stream,
                            Path.GetFileName(file.FullPath),
                            cancellationToken);
                    }

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

            await _api.CompleteBackupJobAsync(
                job.Id,
                bytesSelected,
                bytesUploaded,
                cancellationToken);

            BackupProgress.Value = 100;
            BackupProgressPercent.Text = "100%";

            BackupProgressText.Text =
                $"{filesUploaded:N0} uploaded ({FormatBytes(bytesUploaded)}) • " +
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

    private async void Restore_Click(
        object sender,
        RoutedEventArgs e)
    {
        ShowView(RestoreView);

        RestoreStatusText.Text =
            "Select a backup date.";

        RestoreBackupButton.IsEnabled = false;

        _restoreFiles =
            Array.Empty<BackupFileDto>();

        RestoreFilesGrid.ItemsSource = null;
        RestoreJobComboBox.ItemsSource = null;

        RestoreCalendar.SelectedDates.Clear();

        var completedJobs =
            _jobs
                .Where(x =>
                    x.Status.Equals(
                        "Completed",
                        StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(
                    x => x.CompletedAtUtc ?? x.StartedAtUtc)
                .ToList();

        if (completedJobs.Count == 0)
        {
            RestoreCalendarDetails.Text =
                "No completed backups are available.";

            RestoreStatusText.Text =
                "No completed backups are available.";

            return;
        }

        var backupDates =
            completedJobs
                .Select(x =>
                    (x.CompletedAtUtc ?? x.StartedAtUtc)
                        .ToLocalTime()
                        .Date)
                .Distinct()
                .OrderBy(x => x)
                .ToList();


        RestoreCalendar.DisplayDate =
            backupDates[^1];

        RestoreCalendarDetails.Text =
            $"{backupDates.Count:N0} backup dates available. " +
            "Select a date.";

        RestoreStatusText.Text =
            "Select a date on the calendar.";
    }

    private void RestoreCalendar_DisplayDateChanged(
        object sender,
        CalendarDateChangedEventArgs e)
    {
        UpdateCalendarHighlights();
    }

    private void RestoreCalendar_SelectedDatesChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (RestoreCalendar.SelectedDate is not DateTime selectedDate)
            return;

        var selectedDateOnly =
            selectedDate.Date;

        var matchingJobs =
            _jobs
                .Where(x =>
                    x.Status.Equals(
                        "Completed",
                        StringComparison.OrdinalIgnoreCase))
                .Where(x =>
                    (x.CompletedAtUtc ?? x.StartedAtUtc)
                        .ToLocalTime()
                        .Date == selectedDateOnly)
                .OrderByDescending(
                    x => x.CompletedAtUtc ?? x.StartedAtUtc)
                .ToList();

        RestoreJobComboBox.ItemsSource =
            matchingJobs
                .Select(x => new RestoreJobRow(x))
                .ToList();

        RestoreFilesGrid.ItemsSource = null;

        _restoreFiles =
            Array.Empty<BackupFileDto>();

        RestoreBackupButton.IsEnabled = false;

        if (matchingJobs.Count == 0)
        {
            RestoreCalendarDetails.Text =
                $"{selectedDateOnly:dd MMM yyyy}: no completed backup.";

            RestoreStatusText.Text =
                "No completed backup exists for this date.";

            return;
        }

        RestoreCalendarDetails.Text =
            matchingJobs.Count == 1
                ? $"{selectedDateOnly:dd MMM yyyy}: 1 completed backup."
                : $"{selectedDateOnly:dd MMM yyyy}: {matchingJobs.Count:N0} completed backups.";

        RestoreStatusText.Text =
            "Select a backup.";

        RestoreJobComboBox.SelectedIndex = 0;
    }

    private void UpdateCalendarHighlights()
    {
        // WPF's standard Calendar control does not expose
        // per-date styling through a simple property.
        // The available backup dates are therefore handled
        // through the date selection logic.
    }

    private async void RestoreJobComboBox_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (RestoreJobComboBox.SelectedItem
            is not RestoreJobRow row)
            return;

        try
        {
            RestoreStatusText.Text =
                "Loading files...";

            _restoreFiles =
                await _api.GetBackupFilesAsync(
                    row.Job.Id);

            RestoreFilesGrid.ItemsSource =
                _restoreFiles
                    .Select(x => new RestoreFileRow(x))
                    .ToList();

            RestoreCalendarDetails.Text =
                $"{_restoreFiles.Count:N0} files in this backup.";

            RestoreBackupButton.IsEnabled =
                _restoreFiles.Count > 0;

            RestoreStatusText.Text =
                _restoreFiles.Count > 0
                    ? "Ready to restore."
                    : "This backup contains no files.";
        }
        catch (Exception ex)
        {
            _restoreFiles =
                Array.Empty<BackupFileDto>();

            RestoreFilesGrid.ItemsSource = null;
            RestoreBackupButton.IsEnabled = false;

            RestoreStatusText.Text =
                $"Unable to load files: {ex.Message}";
        }
    }

    private void BrowseRestoreDestination_Click(
        object sender,
        RoutedEventArgs e)
    {
        var dialog =
            new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select restore destination."
            };

        if (dialog.ShowDialog() == true)
        {
            RestoreDestinationText.Text =
                dialog.FolderName;
        }
    }

    private async void RestoreBackup_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (RestoreJobComboBox.SelectedItem
            is not RestoreJobRow row)
            return;

        var destination =
            RestoreDestinationText.Text.Trim();

        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show(
                this,
                "Select a restore destination.",
                "Restore",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        if (_restoreFiles.Count == 0)
        {
            MessageBox.Show(
                this,
                "There are no files available in the selected backup.",
                "Restore",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        var answer =
            MessageBox.Show(
                this,
                $"Restore {_restoreFiles.Count:N0} files to:" +
                $"{Environment.NewLine}{Environment.NewLine}" +
                destination +
                $"{Environment.NewLine}{Environment.NewLine}" +
                "Existing files may be overwritten.",
                "Confirm Restore",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            RestoreBackupButton.IsEnabled = false;
            RestoreStatusText.Text =
                "Restoring files...";

            // Download every file from the server and write it on THIS
            // computer. (The old server-side restore wrote files on the
            // server, which is wrong once the API runs on the VPS.)
            var destinationRoot =
                Path.GetFullPath(destination);

            Directory.CreateDirectory(destinationRoot);

            var rootWithSeparator =
                destinationRoot.EndsWith(Path.DirectorySeparatorChar)
                    ? destinationRoot
                    : destinationRoot + Path.DirectorySeparatorChar;

            var filesRestored = 0;
            long bytesRestored = 0;
            var total = _restoreFiles.Count;

            foreach (var file in _restoreFiles.ToList())
            {
                var relative =
                    file.RelativePath
                        .Replace('/', Path.DirectorySeparatorChar)
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .TrimStart(Path.DirectorySeparatorChar);

                var targetPath =
                    Path.GetFullPath(
                        Path.Combine(destinationRoot, relative));

                if (!targetPath.StartsWith(
                        rootWithSeparator,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Refusing to restore outside the destination: {file.RelativePath}");
                }

                var index = filesRestored + 1;
                var displayPath = file.RelativePath;

                RestoreStatusText.Text =
                    $"Restoring {index:N0} of {total:N0}: {displayPath}";

                var progress = new Progress<long>(bytes =>
                {
                    RestoreStatusText.Text =
                        $"Restoring {index:N0} of {total:N0}: {displayPath} • " +
                        $"{FormatBytes(bytes)}";
                });

                var written =
                    await _api.DownloadFileAsync(
                        row.Job.Id,
                        file.Id,
                        targetPath,
                        progress);

                filesRestored++;
                bytesRestored += written;
            }

            var result =
                new BackupRestoreResultDto(
                    row.Job.Id,
                    destinationRoot,
                    filesRestored,
                    bytesRestored);

            RestoreStatusText.Text =
                $"Restore complete. {result.FilesRestored:N0} files restored. " +
                $"{FormatBytes(result.BytesRestored)}.";

            MessageBox.Show(
                this,
                $"Restore completed successfully." +
                $"{Environment.NewLine}{Environment.NewLine}" +
                $"Files restored: {result.FilesRestored:N0}" +
                $"{Environment.NewLine}" +
                $"Data restored: {FormatBytes(result.BytesRestored)}" +
                $"{Environment.NewLine}" +
                $"Location: {destinationRoot}",
                "Restore Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            RestoreStatusText.Text =
                "Restore failed.";

            MessageBox.Show(
                this,
                ex.Message,
                "Restore Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RestoreBackupButton.IsEnabled =
                _restoreFiles.Count > 0;
        }
    }

    private sealed class RestoreJobRow
    {
        public BackupJobDto Job { get; }

        public string Display { get; }

        public RestoreJobRow(BackupJobDto job)
        {
            Job = job;

            var date =
                (job.CompletedAtUtc ?? job.StartedAtUtc)
                    .ToLocalTime();

            Display =
                $"{date:HH:mm:ss} - {FormatBytes(job.BytesUploaded)}";
        }

        public override string ToString()
        {
            return Display;
        }
    }

    private sealed class RestoreFileRow
    {
        public string RelativePath { get; }

        public string SizeDisplay =>
            "Available";

        public RestoreFileRow(BackupFileDto file)
        {
            RelativePath =
                file.RelativePath;
        }
    }


    private void History_Click(
        object sender,
        RoutedEventArgs e)
    {
        HistoryGrid.ItemsSource =
            _jobs
                .OrderByDescending(
                    x => x.StartedAtUtc)
                .Select(
                    x => new HistoryRow(x))
                .ToList();

        ShowView(HistoryView);
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

        if (!_folders.Contains(
                selectedPath,
                StringComparer.OrdinalIgnoreCase))
        {
            _folders.Add(selectedPath);
            UpdateFolderCount();
        }
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
