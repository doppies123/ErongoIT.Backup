using ErongoIT.Backup.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ErongoIT.Backup.Web.Pages;

public sealed class BackupJobsModel : PageModel
{
    private readonly BackupApiClient _api;

    public BackupJobsModel(BackupApiClient api)
    {
        _api = api;
    }

    public ApiHealth? Health { get; private set; }

    public IReadOnlyList<Customer> Customers { get; private set; }
        = Array.Empty<Customer>();

    public IReadOnlyList<Device> Devices { get; private set; }
        = Array.Empty<Device>();

    public IReadOnlyList<BackupPlan> BackupPlans { get; private set; }
        = Array.Empty<BackupPlan>();

    public IReadOnlyList<BackupJobDisplay> BackupJobs { get; private set; }
        = Array.Empty<BackupJobDisplay>();

    [BindProperty]
    public CreateBackupJobInput CreateJob { get; set; } = new();

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        CancellationToken cancellationToken)
    {
        Console.WriteLine("========== BACKUP JOB POST ==========");
        Console.WriteLine($"CustomerId   : {CreateJob.CustomerId}");
        Console.WriteLine($"DeviceId     : {CreateJob.DeviceId}");
        Console.WriteLine($"BackupPlanId : {CreateJob.BackupPlanId}");
        Console.WriteLine($"Type         : {CreateJob.Type}");

        ValidateCreate();

        if (!ModelState.IsValid)
        {
            Console.WriteLine("========== MODEL STATE INVALID ==========");

            foreach (var entry in ModelState)
            {
                foreach (var error in entry.Value.Errors)
                {
                    Console.WriteLine($"FIELD: {entry.Key}");
                    Console.WriteLine($"ERROR: {error.ErrorMessage}");

                    if (error.Exception != null)
                    {
                        Console.WriteLine(
                            $"EXCEPTION: {error.Exception}");
                    }
                }
            }

            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _api.CreateBackupJobAsync(
                CreateJob.CustomerId,
                CreateJob.DeviceId,
                CreateJob.BackupPlanId,
                CreateJob.Type,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Backup job created successfully.";

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostStartAsync(
        Guid backupJobId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _api.StartBackupJobAsync(
                backupJobId,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Backup job started successfully.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelAsync(
        Guid backupJobId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _api.CancelBackupJobAsync(
                backupJobId,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Backup job cancelled successfully.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostClearHistoryAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _api.ClearBackupJobHistoryAsync(
                deviceId,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Backup job history cleared successfully.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage();
    }


    public async Task<IActionResult> OnPostRecoverStaleAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            var recovered =
                await _api.RecoverStaleBackupJobsAsync(
                    deviceId,
                    30,
                    cancellationToken);

            TempData["SuccessMessage"] =
                recovered == 0
                    ? "No stale backup jobs were found."
                    : $"{recovered} stale backup job(s) recovered.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage();
    }

    private async Task LoadAsync(
        CancellationToken cancellationToken)
    {
        Health = await _api.GetHealthAsync(
            cancellationToken);

        try
        {
            Customers = await _api.GetCustomersAsync(
                cancellationToken);
        }
        catch
        {
            Customers = Array.Empty<Customer>();
        }

        var devices = new List<Device>();
        var plans = new List<BackupPlan>();
        var jobs = new List<BackupJobDisplay>();

        foreach (var customer in Customers)
        {
            try
            {
                var customerDevices =
                    await _api.GetDevicesAsync(
                        customer.Id,
                        cancellationToken);

                devices.AddRange(customerDevices);

                var customerPlans =
                    await _api.GetBackupPlansAsync(
                        customer.Id,
                        cancellationToken);

                plans.AddRange(customerPlans);

                foreach (var device in customerDevices)
                {
                    try
                    {
                        var deviceJobs =
                            await _api.GetBackupJobsAsync(
                                device.Id,
                                cancellationToken);

                        foreach (var job in deviceJobs)
                        {
                            var plan = customerPlans
                                .FirstOrDefault(x =>
                                    x.Id == job.BackupPlanId);

                            jobs.Add(
                                new BackupJobDisplay(
                                    job,
                                    customer,
                                    device,
                                    plan));
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        Devices = devices
            .OrderBy(x => x.Name)
            .ToList();

        BackupPlans = plans
            .Where(x => x.IsEnabled)
            .OrderBy(x => x.Name)
            .ToList();

        BackupJobs = jobs
            .OrderByDescending(x =>
                x.Job.StartedAtUtc)
            .ToList();
    }

    private void ValidateCreate()
    {
        if (CreateJob.CustomerId == Guid.Empty)
        {
            ModelState.AddModelError(
                "CreateJob.CustomerId",
                "Customer is required.");
        }

        if (CreateJob.DeviceId == Guid.Empty)
        {
            ModelState.AddModelError(
                "CreateJob.DeviceId",
                "Device is required.");
        }

        if (CreateJob.BackupPlanId == Guid.Empty)
        {
            ModelState.AddModelError(
                "CreateJob.BackupPlanId",
                "Backup plan is required.");
        }

        if (CreateJob.Type is < 1 or > 3)
        {
            ModelState.AddModelError(
                "CreateJob.Type",
                "A valid backup type is required.");
        }
    }
}

public sealed class CreateBackupJobInput
{
    public Guid CustomerId { get; set; }

    public Guid DeviceId { get; set; }

    public Guid BackupPlanId { get; set; }

    public int Type { get; set; } = 1;
}

public sealed class BackupJobDisplay
{
    public BackupJobDisplay(
        BackupJob job,
        Customer customer,
        Device device,
        BackupPlan? plan)
    {
        Job = job;
        Customer = customer;
        Device = device;
        Plan = plan;
    }

    public BackupJob Job { get; }

    public Customer Customer { get; }

    public Device Device { get; }

    public BackupPlan? Plan { get; }

    public string TypeName =>
        Job.Type switch
        {
            1 => "File",
            2 => "Database",
            3 => "System",
            _ => "Unknown"
        };
}
