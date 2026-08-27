using ErongoIT.Backup.Web.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ErongoIT.Backup.Web.Pages;

public sealed class IndexModel : PageModel
{
    private readonly BackupApiClient _api;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        BackupApiClient api,
        ILogger<IndexModel> logger)
    {
        _api = api;
        _logger = logger;
    }

    public ApiHealth? Health { get; private set; }

    public IReadOnlyList<Customer> Customers { get; private set; }
        = Array.Empty<Customer>();

    public IReadOnlyList<Device> Devices { get; private set; }
        = Array.Empty<Device>();

    public IReadOnlyList<BackupPlan> BackupPlans { get; private set; }
        = Array.Empty<BackupPlan>();

    public IReadOnlyList<BackupJob> BackupJobs { get; private set; }
        = Array.Empty<BackupJob>();

    public string? LoadError { get; private set; }

    public int OnlineDevices =>
        Devices.Count(IsDeviceOnline);

    public int FailedJobs =>
        BackupJobs.Count(x =>
            string.Equals(
                x.Status,
                "Failed",
                StringComparison.OrdinalIgnoreCase));

    public int CompletedJobs =>
        BackupJobs.Count(x =>
            string.Equals(
                x.Status,
                "Completed",
                StringComparison.OrdinalIgnoreCase));

    public int RunningJobs =>
        BackupJobs.Count(x =>
            string.Equals(
                x.Status,
                "Running",
                StringComparison.OrdinalIgnoreCase));

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            Health = await _api.GetHealthAsync(
                cancellationToken);

            if (Health is null)
            {
                LoadError =
                    "The Web application could not connect to the Backup API.";

                return;
            }

            Customers = await _api.GetCustomersAsync(
                cancellationToken);

            foreach (var customer in Customers)
            {
                var devices = await _api.GetDevicesAsync(
                    customer.Id,
                    cancellationToken);

                Devices = Devices
                    .Concat(devices)
                    .ToList();

                var plans = await _api.GetBackupPlansAsync(
                    customer.Id,
                    cancellationToken);

                BackupPlans = BackupPlans
                    .Concat(plans)
                    .ToList();

                foreach (var device in devices)
                {
                    var jobs = await _api.GetBackupJobsAsync(
                        device.Id,
                        cancellationToken);

                    BackupJobs = BackupJobs
                        .Concat(jobs)
                        .ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to load the Backup dashboard.");

            LoadError = ex.ToString();
        }
    }

    private static bool IsDeviceOnline(Device device)
    {
        if (!device.IsActive ||
            !device.LastSeenAtUtc.HasValue)
        {
            return false;
        }

        return DateTime.UtcNow -
               device.LastSeenAtUtc.Value <
               TimeSpan.FromMinutes(2);
    }
}
