using ErongoIT.Backup.Application.Maintenance;

namespace ErongoIT.Backup.Api.Maintenance;

/// <summary>
/// Runs retention cleanup on a timer inside the API.
/// Config: Retention__Enabled (default true),
///         Retention__IntervalHours (default 6),
///         Retention__StartupDelayMinutes (default 10).
/// </summary>
public sealed class RetentionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RetentionBackgroundService> _logger;

    public RetentionBackgroundService(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<RetentionBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var enabled = _configuration.GetValue("Retention:Enabled", true);

        if (!enabled)
        {
            _logger.LogInformation("Retention cleanup is disabled.");
            return;
        }

        var intervalHours = Math.Max(
            _configuration.GetValue("Retention:IntervalHours", 6), 1);

        var startupDelayMinutes = Math.Max(
            _configuration.GetValue("Retention:StartupDelayMinutes", 10), 0);

        _logger.LogInformation(
            "Retention cleanup scheduled every {Hours} hour(s), first run in {Minutes} minute(s).",
            intervalHours,
            startupDelayMinutes);

        try
        {
            await Task.Delay(
                TimeSpan.FromMinutes(startupDelayMinutes),
                stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(
            TimeSpan.FromHours(intervalHours));

        do
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<IRetentionService>();

                var report = await service.RunAsync(
                    dryRun: false,
                    stoppingToken);

                _logger.LogInformation(
                    "Retention cleanup: {Jobs} job(s), {Files} file record(s), {Contents} content file(s) deleted, {Bytes} bytes freed{Skipped}.",
                    report.JobsDeleted,
                    report.FilesDeleted,
                    report.ContentsDeleted,
                    report.BytesFreed,
                    report.ContentCleanupSkipped
                        ? " (content cleanup skipped: backup running)"
                        : string.Empty);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Retention cleanup failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
