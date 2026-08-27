using ErongoIT.Backup.Agent.Api;
using ErongoIT.Backup.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace ErongoIT.Backup.Agent;

public sealed class Worker : BackgroundService
{
    private readonly IBackupApiClient _api;
    private readonly AgentOptions _options;
    private readonly ILogger<Worker> _logger;

    private DateTime _lastHeartbeatUtc = DateTime.MinValue;

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
            "SourcePath: {SourcePath}",
            _options.SourcePath);

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
            _options.StaleJobTimeoutMinutes,
            cancellationToken);

        if (recovered > 0)
        {
            _logger.LogWarning(
                "Recovered {RecoveredJobs} stale backup job(s) for device {DeviceId}.",
                recovered,
                _options.DeviceId);
        }

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
            cancellationToken);
    }

    private async Task CheckPlanAsync(
        BackupPlanDto plan,
        IReadOnlyList<BackupJobDto> jobs,
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

        _logger.LogInformation(
            "Source path: {SourcePath}",
            _options.SourcePath);

        if (!Directory.Exists(
                _options.SourcePath))
        {
            throw new DirectoryNotFoundException(
                $"Backup source directory does not exist: {_options.SourcePath}");
        }

        _logger.LogInformation(
            "Source directory exists.");

        BackupJobDto? job = null;

        try
        {
            _logger.LogInformation(
                "Creating backup job for plan {PlanId}.",
                plan.Id);

            job = await _api.CreateBackupJobAsync(
                _options.CustomerId,
                _options.DeviceId,
                plan.Id,
                type: 1,
                cancellationToken);

            _logger.LogInformation(
                "Created backup job {BackupJobId}.",
                job.Id);

            await _api.StartBackupJobAsync(
                job.Id,
                cancellationToken);

            _logger.LogInformation(
                "Backup job {BackupJobId} is now Running.",
                job.Id);

            var files =
                Directory.EnumerateFiles(
                    _options.SourcePath,
                    "*",
                    SearchOption.AllDirectories)
                .ToList();

            _logger.LogInformation(
                "Found {FileCount} file(s) in source.",
                files.Count);

            long bytesSelected = 0;
            long bytesUploaded = 0;
            var filesUploaded = 0;

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileInfo =
                    new FileInfo(filePath);

                if (!fileInfo.Exists)
                {
                    _logger.LogWarning(
                        "File disappeared before processing: {FilePath}.",
                        filePath);

                    continue;
                }

                var relativePath =
                    Path.GetRelativePath(
                        _options.SourcePath,
                        filePath);

                bytesSelected += fileInfo.Length;

                _logger.LogInformation(
                    "Uploading {RelativePath} ({Bytes} bytes).",
                    relativePath,
                    fileInfo.Length);

                await using var stream =
                    new FileStream(
                        filePath,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 1024 * 1024,
                        useAsync: true);

                await _api.UploadFileAsync(
                    job.Id,
                    _options.CustomerId,
                    _options.DeviceId,
                    relativePath,
                    stream,
                    Path.GetFileName(filePath),
                    cancellationToken);

                bytesUploaded += fileInfo.Length;
                filesUploaded++;
            }

            await _api.CompleteBackupJobAsync(
                job.Id,
                bytesSelected,
                bytesUploaded,
                cancellationToken);

            _logger.LogInformation(
                "========== BACKUP COMPLETED ==========");

            _logger.LogInformation(
                "Job={BackupJobId}, Files={FilesUploaded}, Selected={BytesSelected}, Uploaded={BytesUploaded}.",
                job.Id,
                filesUploaded,
                bytesSelected,
                bytesUploaded);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "========== BACKUP FAILED ==========");

            if (job is not null)
            {
                try
                {
                    await _api.FailBackupJobAsync(
                        job.Id,
                        ex.Message,
                        cancellationToken);
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
}
