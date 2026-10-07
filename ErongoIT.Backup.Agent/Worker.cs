using ErongoIT.Backup.Agent.Api;
using ErongoIT.Backup.Agent.Backup;
using ErongoIT.Backup.Agent.Configuration;
using ErongoIT.Backup.Agent.Performance;
using Microsoft.Extensions.Options;

namespace ErongoIT.Backup.Agent;

public sealed class Worker : BackgroundService
{
    private readonly IBackupApiClient _api;
    private readonly AgentOptions _options;
    private readonly ILogger<Worker> _logger;

    private DateTime _lastHeartbeatUtc = DateTime.MinValue;

    // After a restart, any job still "Running" for this PC was cut off
    // (service stopped, PC shut down, upgrade). Close it straight away
    // instead of waiting StaleJobTimeoutMinutes, which blocked backups.
    private bool _startupRecoveryDone;

    public Worker(
        IBackupApiClient api,
        IOptions<AgentOptions> options,
        ILogger<Worker> logger)
    {
        _api = api;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "============================================================");

        _logger.LogInformation(
            "ErongoIT Backup Agent starting.");

        _logger.LogInformation(
            "CustomerId: {CustomerId}",
            _options.CustomerId);

        _logger.LogInformation(
            "DeviceId: {DeviceId}",
            _options.DeviceId);

        _logger.LogInformation(
            "Configured BackupPlanId: {BackupPlanId}",
            _options.BackupPlanId);

        _logger.LogInformation(
            "Folders: {Folders}",
            string.Join("; ", _options.GetSourceFolders()));

        _logger.LogInformation(
            "Authentication: {Mode}",
            _options.UsesDeviceKey ? "device key (enrolled)" : "admin login (developer)");

        // Keep the user's PC responsive while backing up.
        if (_options.BackgroundMode)
        {
            _logger.LogInformation(
                "Priority: {Priority}",
                BackgroundPriority.Enter());
        }

        IoThrottle.Current = new IoThrottle(
            _options.MaxReadMegabytesPerSecond,
            _options.BusyReadMegabytesPerSecond,
            _options.BusyCpuPercent);

        _logger.LogInformation(
            "Disk read limit: {Limit}",
            IoThrottle.Current.Describe());

        _logger.LogInformation(
            "Battery: {Battery}",
            _options.MinimumBatteryPercent > 0
                ? $"scheduled backups wait while on battery below {_options.MinimumBatteryPercent}%"
                : "backups run on battery");

        _logger.LogInformation(
            "============================================================");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendHeartbeatIfDueAsync(
                    stoppingToken);

                await CheckBackupScheduleAsync(
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Agent scheduler iteration failed.");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(
                    Math.Max(
                        5,
                        _options.SchedulerIntervalSeconds)),
                stoppingToken);
        }

        _logger.LogInformation(
            "ErongoIT Backup Agent stopped.");
    }

    private async Task SendHeartbeatIfDueAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        if ((now - _lastHeartbeatUtc).TotalSeconds
            < Math.Max(
                10,
                _options.HeartbeatIntervalSeconds))
        {
            return;
        }

        _logger.LogInformation(
            "Sending heartbeat for device {DeviceId}.",
            _options.DeviceId);

        await _api.SendHeartbeatAsync(
            _options.DeviceId,
            _options.AgentVersion,
            cancellationToken);

        _lastHeartbeatUtc = now;

        _logger.LogInformation(
            "Heartbeat sent for device {DeviceId}.",
            _options.DeviceId);
    }

    private async Task CheckBackupScheduleAsync(
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Loading device configuration for device {DeviceId}.",
            _options.DeviceId);

        var device = await _api.GetDeviceAsync(
            _options.DeviceId,
            cancellationToken);

        if (device is null)
        {
            _logger.LogError(
                "Device {DeviceId} was not found in the Backup API.",
                _options.DeviceId);

            return;
        }

        if (device.CustomerId != _options.CustomerId)
        {
            _logger.LogError(
                "Device {DeviceId} belongs to customer {DeviceCustomerId}, not configured customer {CustomerId}.",
                device.Id,
                device.CustomerId,
                _options.CustomerId);

            return;
        }

        if (!device.IsActive)
        {
            _logger.LogInformation(
                "Device {DeviceId} is inactive. Backup scheduling is disabled.",
                device.Id);

            return;
        }

        var assignedBackupPlanId =
            device.AssignedBackupPlanId;

        if (!assignedBackupPlanId.HasValue ||
            assignedBackupPlanId.Value == Guid.Empty)
        {
            _logger.LogInformation(
                "Device {DeviceId} has no backup plan assigned. No backup will run.",
                device.Id);

            return;
        }

        _logger.LogInformation(
            "Device {DeviceId} is assigned backup plan {BackupPlanId}.",
            device.Id,
            assignedBackupPlanId.Value);

        var recovered = await _api.RecoverStaleJobsAsync(
            _options.DeviceId,
            _startupRecoveryDone ? _options.StaleJobTimeoutMinutes : 1,
            cancellationToken);

        if (recovered > 0)
        {
            _logger.LogWarning(
                _startupRecoveryDone
                    ? "Recovered {RecoveredJobs} stale backup job(s) for device {DeviceId}."
                    : "Closed {RecoveredJobs} backup job(s) for device {DeviceId} that were interrupted before this agent started.",
                recovered,
                _options.DeviceId);
        }

        _startupRecoveryDone = true;

        var plans = await _api.GetBackupPlansAsync(
            _options.CustomerId,
            cancellationToken);

        var plan = plans.FirstOrDefault(
            x => x.Id == assignedBackupPlanId.Value);

        if (plan is null)
        {
            _logger.LogWarning(
                "Assigned backup plan {BackupPlanId} was not found for customer {CustomerId}.",
                assignedBackupPlanId.Value,
                _options.CustomerId);

            return;
        }

        if (!plan.IsEnabled)
        {
            _logger.LogInformation(
                "Assigned backup plan {PlanName} ({PlanId}) is disabled.",
                plan.Name,
                plan.Id);

            return;
        }

        if (plan.CustomerId != device.CustomerId)
        {
            _logger.LogError(
                "Assigned backup plan {PlanId} belongs to customer {PlanCustomerId}, not device customer {DeviceCustomerId}.",
                plan.Id,
                plan.CustomerId,
                device.CustomerId);

            return;
        }

        var jobs = await _api.GetBackupJobsAsync(
            _options.DeviceId,
            cancellationToken);

        await CheckPlanAsync(
            plan,
            jobs,
            device.BackupRequestedAtUtc,
            cancellationToken);
    }

    private async Task CheckPlanAsync(
        BackupPlanDto plan,
        IReadOnlyList<BackupJobDto> jobs,
        DateTime? backupRequestedAtUtc,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Checking plan {PlanId}: {PlanName}. Schedule={ScheduleType}, Interval={IntervalMinutes}, Time={ScheduleTimeMinutes}, Day={ScheduleDayOfWeek}, Retention={RetentionDays}.",
            plan.Id,
            plan.Name,
            GetScheduleTypeName(plan.ScheduleType),
            plan.IntervalMinutes,
            FormatScheduleTime(plan.ScheduleTimeMinutes),
            plan.ScheduleDayOfWeek,
            plan.RetentionDays);

        var activeJob = jobs.FirstOrDefault(
            x =>
                x.BackupPlanId == plan.Id &&
                (x.Status == "Pending" ||
                 x.Status == "Running"));

        if (activeJob is not null)
        {
            _logger.LogInformation(
                "Plan {PlanId} already has active job {BackupJobId} in {Status}.",
                plan.Id,
                activeJob.Id,
                activeJob.Status);

            return;
        }

        var latestCompleted = jobs
            .Where(
                x =>
                    x.BackupPlanId == plan.Id &&
                    x.Status == "Completed" &&
                    x.CompletedAtUtc.HasValue)
            .OrderByDescending(
                x => x.CompletedAtUtc)
            .FirstOrDefault();

        // "Back up now" pressed (Agent GUI or portal) since the last backup started?
        var lastStartUtc = jobs
            .Select(x => (DateTime?)x.StartedAtUtc)
            .Max() ?? DateTime.MinValue;

        if (backupRequestedAtUtc is DateTime requestedUtc &&
            requestedUtc > lastStartUtc)
        {
            _logger.LogInformation(
                "Backup requested at {RequestedLocal} (Back up now). Starting.",
                requestedUtc.ToLocalTime());

            await RunBackupAsync(
                plan,
                cancellationToken);

            return;
        }

        var nowLocal = DateTime.Now;

        if (!IsPlanDue(
                plan,
                latestCompleted,
                nowLocal))
        {
            var nextRun = CalculateNextRunLocal(
                plan,
                latestCompleted,
                nowLocal);

            _logger.LogInformation(
                "Plan {PlanId} is not due. Current local time={NowLocal}. Next run={NextRunLocal}.",
                plan.Id,
                nowLocal,
                nextRun);

            return;
        }

        if (_options.MinimumBatteryPercent > 0 &&
            PowerStatus.IsOnBattery(out var batteryPercent) &&
            batteryPercent >= 0 &&
            batteryPercent < _options.MinimumBatteryPercent)
        {
            _logger.LogInformation(
                "Plan {PlanId} is due, but the PC is on battery at {Battery}% (minimum {Minimum}%). Waiting for power.",
                plan.Id,
                batteryPercent,
                _options.MinimumBatteryPercent);

            return;
        }

        _logger.LogInformation(
            "Plan {PlanId} is due for backup.",
            plan.Id);

        await RunBackupAsync(
            plan,
            cancellationToken);
    }

    private static bool IsPlanDue(
        BackupPlanDto plan,
        BackupJobDto? latestCompleted,
        DateTime nowLocal)
    {
        if (latestCompleted?.CompletedAtUtc is not DateTime completedUtc)
        {
            return true;
        }

        var nextRun = CalculateNextRunLocal(
            plan,
            latestCompleted,
            nowLocal);

        return nowLocal >= nextRun;
    }

    private static DateTime CalculateNextRunLocal(
        BackupPlanDto plan,
        BackupJobDto? latestCompleted,
        DateTime nowLocal)
    {
        switch (plan.ScheduleType)
        {
            case 1:
            {
                var completedLocal =
                    latestCompleted?.CompletedAtUtc?
                        .ToLocalTime()
                    ?? nowLocal;

                return completedLocal.AddMinutes(
                    Math.Max(
                        15,
                        plan.IntervalMinutes));
            }

            case 2:
            {
                var scheduledToday =
                    nowLocal.Date.AddMinutes(
                        Math.Clamp(
                            plan.ScheduleTimeMinutes,
                            0,
                            1439));

                if (latestCompleted?.CompletedAtUtc is not DateTime completedUtc)
                    return scheduledToday;

                var completedLocal =
                    completedUtc.ToLocalTime();

                var next =
                    completedLocal.Date.AddMinutes(
                        Math.Clamp(
                            plan.ScheduleTimeMinutes,
                            0,
                            1439));

                if (completedLocal >= next)
                    next = next.AddDays(1);

                return next;
            }

            case 3:
            {
                var scheduledTime =
                    Math.Clamp(
                        plan.ScheduleTimeMinutes,
                        0,
                        1439);

                var targetDay =
                    Math.Clamp(
                        plan.ScheduleDayOfWeek,
                        0,
                        6);

                if (latestCompleted?.CompletedAtUtc is not DateTime completedUtc)
                {
                    var daysUntil =
                        (targetDay -
                         (int)nowLocal.DayOfWeek +
                         7) % 7;

                    var first =
                        nowLocal.Date
                            .AddDays(daysUntil)
                            .AddMinutes(scheduledTime);

                    if (first < nowLocal)
                        first = first.AddDays(7);

                    return first;
                }

                var completedLocal =
                    completedUtc.ToLocalTime();

                var days =
                    (targetDay -
                     (int)completedLocal.DayOfWeek +
                     7) % 7;

                var next =
                    completedLocal.Date
                        .AddDays(days)
                        .AddMinutes(scheduledTime);

                if (next <= completedLocal)
                    next = next.AddDays(7);

                return next;
            }

            default:
                throw new InvalidOperationException(
                    $"Unsupported backup schedule type: {plan.ScheduleType}.");
        }
    }

    private static string GetScheduleTypeName(
        int scheduleType)
    {
        return scheduleType switch
        {
            1 => "Continuous",
            2 => "Daily",
            3 => "Weekly",
            _ => "Unknown"
        };
    }

    private static string FormatScheduleTime(
        int minutes)
    {
        minutes = Math.Clamp(
            minutes,
            0,
            1439);

        return TimeSpan
            .FromMinutes(minutes)
            .ToString(@"hh\:mm");
    }

    /// <summary>
    /// Folders to back up. Re-read from agent.json before every backup so
    /// that folders added or removed in the Agent GUI take effect without
    /// restarting the service.
    /// </summary>
    private IReadOnlyList<string> GetCurrentSourceFolders()
    {
        if (_options.UsesDeviceKey)
        {
            var config = AgentConfigFile.TryLoad();

            if (config is not null &&
                config.DeviceId == _options.DeviceId &&
                config.SourcePaths.Count > 0)
            {
                var latest = new AgentOptions
                {
                    SourcePaths = new List<string>(config.SourcePaths)
                }.GetSourceFolders();

                if (!latest.SequenceEqual(_options.GetSourceFolders(), StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Folder list changed in agent.json: {Folders}",
                        string.Join("; ", latest));

                    _options.SourcePaths = latest.ToList();
                    _options.SourcePath = string.Empty;
                }
            }
        }

        return _options.GetSourceFolders();
    }

    private async Task RunBackupAsync(
        BackupPlanDto plan,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "========== BACKUP START ==========");

        _logger.LogInformation(
            "Plan: {PlanName}",
            plan.Name);

        _logger.LogInformation(
            "Schedule: {ScheduleType}",
            GetScheduleTypeName(
                plan.ScheduleType));

        var sourceFolders = GetCurrentSourceFolders();

        foreach (var folder in sourceFolders)
        {
            _logger.LogInformation(
                "Folder: {Folder} ({State})",
                folder,
                Directory.Exists(folder) ? "found" : "MISSING - skipped");
        }

        var existingFolders = sourceFolders
            .Where(Directory.Exists)
            .ToList();

        var missingFolders = sourceFolders
            .Except(existingFolders, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (existingFolders.Count == 0)
        {
            throw new DirectoryNotFoundException(
                "None of the folders to back up exist: " +
                string.Join("; ", sourceFolders));
        }

        BackupJobDto? job = null;
        SyncManifest? manifest = null;

        try
        {
            job = await _api.CreateBackupJobAsync(
                _options.CustomerId,
                _options.DeviceId,
                plan.Id,
                type: 1,
                cancellationToken);

            await _api.StartBackupJobAsync(
                job.Id,
                cancellationToken);

            _logger.LogInformation(
                "Backup job {BackupJobId} is now Running.",
                job.Id);

            // 1. What the server already has for this PC.
            manifest = await LoadManifestAsync(cancellationToken);

            // 2. Scan the folders: name, size and date modified only (cheap).
            var scanned = ScanFolders(existingFolders);

            long bytesSelected = scanned.Sum(x => x.Length);

            var changedCandidates = scanned
                .Where(x => !manifest.IsUnchanged(x.ServerPath, x.Length, x.LastWriteUtc))
                .ToList();

            // Files the server has that are gone from disk. Files inside a
            // folder that is temporarily missing (USB drive unplugged) are
            // kept, not treated as deleted.
            var scannedPaths = scanned
                .Select(x => x.ServerPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var missingPrefixes = missingFolders
                .Select(f => SyncManifest.ToServerPath(f) + "/")
                .ToList();

            var deleted = manifest.Files.Keys
                .Where(p => !scannedPaths.Contains(p))
                .Where(p => !missingPrefixes.Any(m => p.StartsWith(m, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            _logger.LogInformation(
                "Found {FileCount} file(s) ({Bytes} bytes). New or changed: {Changed}. Deleted: {Deleted}.",
                scanned.Count,
                bytesSelected,
                changedCandidates.Count,
                deleted.Count);

            // 3. Send new and changed files.
            var totals = await SendChangesAsync(
                job.Id,
                manifest,
                changedCandidates,
                bytesSelected,
                cancellationToken);

            // 4. Record deletions.
            foreach (var batch in deleted.Chunk(ChangeBatchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var result = await _api.ApplyChangesAsync(
                    _options.DeviceId,
                    job.Id,
                    Array.Empty<FileChangeDto>(),
                    batch,
                    cancellationToken);

                foreach (var path in batch)
                    manifest.Files.Remove(path);

                totals.Deleted += result.Deleted;
            }

            manifest.Save(AgentConfigFile.DataDirectory);

            _logger.LogInformation(
                "Transfer: {Uploaded} bytes of new content sent as {Transferred} bytes.",
                totals.BytesUploaded,
                totals.BytesTransferred);

            _logger.LogInformation(
                "Result: {Added} added, {Changed} changed, {Deleted} deleted, {Skipped} skipped (unreadable or changing). {Unchanged} unchanged.",
                totals.Added,
                totals.Changed,
                totals.Deleted,
                totals.Skipped,
                scanned.Count - changedCandidates.Count);

            await _api.CompleteBackupJobAsync(
                job.Id,
                bytesSelected,
                totals.BytesUploaded,
                cancellationToken);

            _logger.LogInformation(
                "========== BACKUP COMPLETED ==========");
        }
        catch (Exception ex)
        {
            // Keep what was already sent: the next backup continues from there.
            manifest?.Save(AgentConfigFile.DataDirectory);

            _logger.LogError(
                ex,
                "========== BACKUP FAILED ==========");

            if (job is not null)
            {
                try
                {
                    // Not the stopping token: the job must still be closed
                    // when the service is stopping.
                    using var failTimeout = new CancellationTokenSource(
                        TimeSpan.FromSeconds(15));

                    await _api.FailBackupJobAsync(
                        job.Id,
                        ex is OperationCanceledException && cancellationToken.IsCancellationRequested
                            ? "Backup interrupted: the backup service was stopped or restarted."
                            : ex.Message,
                        failTimeout.Token);
                }
                catch (Exception failException)
                {
                    _logger.LogError(
                        failException,
                        "Failed to mark backup job {BackupJobId} as failed.",
                        job.Id);
                }
            }

            throw;
        }
    }

    private const int ChangeBatchSize = 500;

    private sealed record ScannedFile(
        string FullPath,
        string ServerPath,
        long Length,
        DateTime LastWriteUtc);

    private sealed class BackupTotals
    {
        public int Added;
        public int Changed;
        public int Deleted;
        public int Skipped;
        public long BytesUploaded;
        public long BytesTransferred;
    }

    /// <summary>
    /// Loads the local list of what the server holds; rebuilds it from the
    /// server when missing, from another device or older than a week.
    /// </summary>
    private async Task<SyncManifest> LoadManifestAsync(
        CancellationToken cancellationToken)
    {
        var manifest = SyncManifest.Load(AgentConfigFile.DataDirectory);

        if (!manifest.NeedsFullSync(_options.DeviceId))
            return manifest;

        _logger.LogInformation(
            "Rebuilding the local file list from the server (first run, or weekly check).");

        var rebuilt = new SyncManifest
        {
            DeviceId = _options.DeviceId,
            FullSyncUtc = DateTime.UtcNow
        };

        string? after = null;

        do
        {
            var page = await _api.GetSyncStateAsync(
                _options.DeviceId,
                after,
                5000,
                cancellationToken);

            foreach (var file in page.Files)
            {
                rebuilt.Files[file.Path] = new SyncManifest.Entry(
                    file.Sha256,
                    file.SizeBytes,
                    (file.LastWriteUtc ?? DateTime.MinValue).ToUniversalTime().Ticks);
            }

            after = page.NextAfterPath;
        }
        while (after is not null);

        _logger.LogInformation(
            "The server holds {Count} file(s) for this PC.",
            rebuilt.Files.Count);

        rebuilt.Save(AgentConfigFile.DataDirectory);

        return rebuilt;
    }

    private List<ScannedFile> ScanFolders(
        IReadOnlyList<string> folders)
    {
        var files = new List<ScannedFile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var folder in folders)
        {
            try
            {
                var paths = Directory.EnumerateFiles(
                    folder,
                    "*",
                    new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint
                    });

                foreach (var path in paths)
                {
                    var info = new FileInfo(path);

                    if (!info.Exists)
                        continue;

                    var serverPath = SyncManifest.ToServerPath(info.FullName);

                    // Overlapping folders (C:\Data and C:\Data\Sub): once.
                    if (!seen.Add(serverPath))
                        continue;

                    files.Add(new ScannedFile(
                        info.FullName,
                        serverPath,
                        info.Length,
                        info.LastWriteTimeUtc));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(
                    "Could not fully scan {Folder}: {Message}",
                    folder,
                    ex.Message);
            }
        }

        return files;
    }

    private async Task<BackupTotals> SendChangesAsync(
        Guid jobId,
        SyncManifest manifest,
        IReadOnlyList<ScannedFile> candidates,
        long bytesSelected,
        CancellationToken cancellationToken)
    {
        var totals = new BackupTotals();

        if (candidates.Count == 0)
            return totals;

        var hashCache = FileHashCache.Load(
            "agent",
            directory: AgentConfigFile.DataDirectory);

        long bytesProcessed = bytesSelected - candidates.Sum(x => x.Length);
        var batchNumber = 0;

        foreach (var batch in candidates.Chunk(ChangeBatchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();
            batchNumber++;

            // a. Fingerprint the changed files (unchanged ones were never read).
            var hashed = new List<(ScannedFile File, string Sha256)>();

            foreach (var file in batch)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var sha256 = await hashCache.GetSha256Async(
                        file.FullPath,
                        cancellationToken);

                    hashed.Add((file, sha256));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    totals.Skipped++;

                    _logger.LogWarning(
                        "Skipping unreadable file {Path}: {Message}",
                        file.FullPath,
                        ex.Message);
                }
            }

            // b. Which contents does the server not have yet? (de-duplication)
            var missing = (await _api.CheckContentAsync(
                    _options.DeviceId,
                    hashed
                        .Select(x => new ContentReferenceDto(x.Sha256, x.File.Length))
                        .DistinctBy(x => x.Sha256)
                        .ToList(),
                    cancellationToken))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // c. Upload each missing content once.
            var failedUploads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in hashed.Where(x => missing.Contains(x.Sha256)).GroupBy(x => x.Sha256))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = group.First().File;

                try
                {
                    _logger.LogInformation(
                        "Uploading {Path} ({Bytes} bytes).",
                        file.FullPath,
                        file.Length);

                    await using var prepared = await UploadCompression.PrepareAsync(
                        file.FullPath,
                        cancellationToken);

                    await using (var stream = FileHashing.OpenForUpload(prepared.UploadPath))
                    {
                        await _api.UploadContentAsync(
                            _options.DeviceId,
                            group.Key,
                            stream,
                            prepared.Encoding,
                            cancellationToken);
                    }

                    totals.BytesUploaded += file.Length;
                    totals.BytesTransferred += prepared.UploadLength;
                }
                catch (Exception ex) when (
                    ex is IOException or UnauthorizedAccessException or HttpRequestException &&
                    !cancellationToken.IsCancellationRequested)
                {
                    // Typically the file changed while being read. Try again next backup.
                    failedUploads.Add(group.Key);
                    totals.Skipped += group.Count();

                    _logger.LogWarning(
                        "Could not upload {Path}; it will be retried at the next backup: {Message}",
                        file.FullPath,
                        ex.Message);
                }
            }

            // d. Record the new versions.
            var changes = hashed
                .Where(x => !failedUploads.Contains(x.Sha256))
                .Select(x => new FileChangeDto(
                    x.File.ServerPath,
                    x.Sha256,
                    x.File.Length,
                    x.File.LastWriteUtc))
                .ToList();

            if (changes.Count > 0)
            {
                var result = await _api.ApplyChangesAsync(
                    _options.DeviceId,
                    jobId,
                    changes,
                    Array.Empty<string>(),
                    cancellationToken);

                var notRecorded = result.MissingContentPaths
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                totals.Added += result.Added;
                totals.Changed += result.Changed;
                totals.Skipped += notRecorded.Count;

                foreach (var (file, sha256) in hashed)
                {
                    if (failedUploads.Contains(sha256) || notRecorded.Contains(file.ServerPath))
                        continue;

                    manifest.Set(file.ServerPath, sha256, file.Length, file.LastWriteUtc);
                }
            }

            bytesProcessed += batch.Sum(x => x.Length);

            // Keep progress if the PC shuts down halfway through a big first backup.
            if (batchNumber % 10 == 0)
            {
                manifest.Save(AgentConfigFile.DataDirectory);
                hashCache.Save();
            }

            try
            {
                await _api.UpdateProgressAsync(
                    jobId,
                    bytesSelected,
                    Math.Min(bytesProcessed, bytesSelected),
                    cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(
                    "Progress update failed: {Message}",
                    ex.Message);
            }
        }

        hashCache.Save();

        return totals;
    }
}
