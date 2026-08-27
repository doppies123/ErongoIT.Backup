using ErongoIT.Backup.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ErongoIT.Backup.Web.Pages;

public sealed class BackupPlansModel : PageModel
{
    private readonly BackupApiClient _api;

    public BackupPlansModel(BackupApiClient api)
    {
        _api = api;
    }

    public ApiHealth? Health { get; private set; }

    public IReadOnlyList<Customer> Customers { get; private set; }
        = Array.Empty<Customer>();

    public IReadOnlyList<BackupPlan> BackupPlans { get; private set; }
        = Array.Empty<BackupPlan>();

    [BindProperty]
    public CreateBackupPlanInput CreatePlan { get; set; } = new();

    [BindProperty]
    public EditBackupPlanInput EditPlan { get; set; } = new();

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        CancellationToken cancellationToken)
    {
        NormalizeCreate();

        ValidateCreate();

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _api.CreateBackupPlanAsync(
                CreatePlan.CustomerId,
                CreatePlan.Name.Trim(),
                CreatePlan.ScheduleType,
                CreatePlan.IntervalMinutes,
                CreatePlan.ScheduleTimeMinutes,
                CreatePlan.ScheduleDayOfWeek,
                CreatePlan.RetentionDays,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Backup plan created successfully.";

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostUpdateAsync(
        CancellationToken cancellationToken)
    {
        NormalizeEdit();

        ValidateEdit();

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _api.UpdateBackupPlanScheduleAsync(
                EditPlan.BackupPlanId,
                EditPlan.ScheduleType,
                EditPlan.IntervalMinutes,
                EditPlan.ScheduleTimeMinutes,
                EditPlan.ScheduleDayOfWeek,
                EditPlan.RetentionDays,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Backup plan schedule updated successfully.";

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostEnableAsync(
        Guid backupPlanId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _api.EnableBackupPlanAsync(
                backupPlanId,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Backup plan enabled successfully.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDisableAsync(
        Guid backupPlanId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _api.DisableBackupPlanAsync(
                backupPlanId,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Backup plan disabled successfully.";
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

        var plans = new List<BackupPlan>();

        foreach (var customer in Customers)
        {
            try
            {
                var customerPlans =
                    await _api.GetBackupPlansAsync(
                        customer.Id,
                        cancellationToken);

                plans.AddRange(customerPlans);
            }
            catch
            {
            }
        }

        BackupPlans = plans
            .OrderBy(x => x.Name)
            .ToList();
    }

    private void NormalizeCreate()
    {
        if (CreatePlan.ScheduleType != 1)
            CreatePlan.IntervalMinutes = 0;

        if (CreatePlan.ScheduleType == 1)
        {
            CreatePlan.ScheduleTimeMinutes = 0;
            CreatePlan.ScheduleDayOfWeek = 0;
        }
        else if (CreatePlan.ScheduleType == 2)
        {
            CreatePlan.ScheduleDayOfWeek = 0;
        }
    }

    private void NormalizeEdit()
    {
        if (EditPlan.ScheduleType != 1)
            EditPlan.IntervalMinutes = 0;

        if (EditPlan.ScheduleType == 1)
        {
            EditPlan.ScheduleTimeMinutes = 0;
            EditPlan.ScheduleDayOfWeek = 0;
        }
        else if (EditPlan.ScheduleType == 2)
        {
            EditPlan.ScheduleDayOfWeek = 0;
        }
    }

    private void ValidateCreate()
    {
        if (CreatePlan.CustomerId == Guid.Empty)
        {
            ModelState.AddModelError(
                "CreatePlan.CustomerId",
                "Customer is required.");
        }

        if (string.IsNullOrWhiteSpace(CreatePlan.Name))
        {
            ModelState.AddModelError(
                "CreatePlan.Name",
                "Plan name is required.");
        }

        if (CreatePlan.ScheduleType is < 1 or > 3)
        {
            ModelState.AddModelError(
                "CreatePlan.ScheduleType",
                "A valid schedule type is required.");
        }

        if (CreatePlan.ScheduleType == 1 &&
            CreatePlan.IntervalMinutes < 15)
        {
            ModelState.AddModelError(
                "CreatePlan.IntervalMinutes",
                "Continuous backup interval must be at least 15 minutes.");
        }

        if (CreatePlan.ScheduleType != 1 &&
            (CreatePlan.ScheduleTimeMinutes < 0 ||
             CreatePlan.ScheduleTimeMinutes > 1439))
        {
            ModelState.AddModelError(
                "CreatePlan.ScheduleTimeMinutes",
                "Scheduled time must be between 00:00 and 23:59.");
        }

        if (CreatePlan.ScheduleType == 3 &&
            (CreatePlan.ScheduleDayOfWeek < 0 ||
             CreatePlan.ScheduleDayOfWeek > 6))
        {
            ModelState.AddModelError(
                "CreatePlan.ScheduleDayOfWeek",
                "Weekly schedule day must be between Sunday and Saturday.");
        }

        if (CreatePlan.RetentionDays <= 0)
        {
            ModelState.AddModelError(
                "CreatePlan.RetentionDays",
                "Retention must be greater than zero days.");
        }
    }

    private void ValidateEdit()
    {
        if (EditPlan.BackupPlanId == Guid.Empty)
        {
            ModelState.AddModelError(
                string.Empty,
                "Backup plan ID is required.");
        }

        if (EditPlan.ScheduleType is < 1 or > 3)
        {
            ModelState.AddModelError(
                "EditPlan.ScheduleType",
                "A valid schedule type is required.");
        }

        if (EditPlan.ScheduleType == 1 &&
            EditPlan.IntervalMinutes < 15)
        {
            ModelState.AddModelError(
                "EditPlan.IntervalMinutes",
                "Continuous backup interval must be at least 15 minutes.");
        }

        if (EditPlan.ScheduleType != 1 &&
            (EditPlan.ScheduleTimeMinutes < 0 ||
             EditPlan.ScheduleTimeMinutes > 1439))
        {
            ModelState.AddModelError(
                "EditPlan.ScheduleTimeMinutes",
                "Scheduled time must be between 00:00 and 23:59.");
        }

        if (EditPlan.ScheduleType == 3 &&
            (EditPlan.ScheduleDayOfWeek < 0 ||
             EditPlan.ScheduleDayOfWeek > 6))
        {
            ModelState.AddModelError(
                "EditPlan.ScheduleDayOfWeek",
                "Weekly schedule day must be between Sunday and Saturday.");
        }

        if (EditPlan.RetentionDays <= 0)
        {
            ModelState.AddModelError(
                "EditPlan.RetentionDays",
                "Retention must be greater than zero days.");
        }
    }
}

public sealed class CreateBackupPlanInput
{
    public Guid CustomerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int ScheduleType { get; set; } = 1;

    public int IntervalMinutes { get; set; } = 60;

    public int ScheduleTimeMinutes { get; set; } = 0;

    public int ScheduleDayOfWeek { get; set; } = 0;

    public int RetentionDays { get; set; } = 30;
}

public sealed class EditBackupPlanInput
{
    public Guid BackupPlanId { get; set; }

    public int ScheduleType { get; set; } = 1;

    public int IntervalMinutes { get; set; } = 60;

    public int ScheduleTimeMinutes { get; set; } = 0;

    public int ScheduleDayOfWeek { get; set; } = 0;

    public int RetentionDays { get; set; } = 30;
}
