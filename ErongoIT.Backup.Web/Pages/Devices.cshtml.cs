using ErongoIT.Backup.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ErongoIT.Backup.Web.Pages;

public sealed class DevicesModel : PageModel
{
    private readonly BackupApiClient _api;

    public DevicesModel(BackupApiClient api)
    {
        _api = api;
    }

    public ApiHealth? Health { get; private set; }

    public IReadOnlyList<Customer> Customers { get; private set; }
        = Array.Empty<Customer>();

    public IReadOnlyList<Device> Devices { get; private set; }
        = Array.Empty<Device>();

    [BindProperty]
    public CreateDeviceInput CreateDevice { get; set; }
        = new();

    [BindProperty]
    public RenameDeviceInput RenameDevice { get; set; }
        = new();

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        CancellationToken cancellationToken)
    {
        if (CreateDevice.CustomerId == Guid.Empty)
        {
            ModelState.AddModelError(
                "CreateDevice.CustomerId",
                "Customer is required.");
        }

        if (string.IsNullOrWhiteSpace(CreateDevice.Name))
        {
            ModelState.AddModelError(
                "CreateDevice.Name",
                "Device name is required.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _api.CreateDeviceAsync(
                CreateDevice.CustomerId,
                CreateDevice.Name.Trim(),
                Normalize(CreateDevice.Hostname),
                Normalize(CreateDevice.OperatingSystem),
                cancellationToken);

            TempData["SuccessMessage"] =
                "Device created successfully.";

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(
                string.Empty,
                ex.Message);

            await LoadAsync(cancellationToken);
            return Page();
        }
    }

    public async Task<IActionResult> OnPostRenameAsync(
        CancellationToken cancellationToken)
    {
        if (RenameDevice.DeviceId == Guid.Empty)
        {
            ModelState.AddModelError(
                string.Empty,
                "Device ID is required.");
        }

        if (string.IsNullOrWhiteSpace(RenameDevice.Name))
        {
            ModelState.AddModelError(
                string.Empty,
                "Device name is required.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _api.RenameDeviceAsync(
                RenameDevice.DeviceId,
                RenameDevice.Name.Trim(),
                cancellationToken);

            TempData["SuccessMessage"] =
                "Device renamed successfully.";

            return RedirectToPage();
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
            return RedirectToPage();
        }
    }

    public async Task<IActionResult> OnPostActivateAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _api.ActivateDeviceAsync(
                deviceId,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Device activated successfully.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeactivateAsync(
        Guid deviceId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _api.DeactivateDeviceAsync(
                deviceId,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Device deactivated successfully.";
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

        foreach (var customer in Customers)
        {
            try
            {
                var customerDevices = await _api.GetDevicesAsync(
                    customer.Id,
                    cancellationToken);

                devices.AddRange(customerDevices);
            }
            catch
            {
            }
        }

        Devices = devices;
    }

    private static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim();
    }
}

public sealed class CreateDeviceInput
{
    public Guid CustomerId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Hostname { get; set; }

    public string? OperatingSystem { get; set; }
}

public sealed class RenameDeviceInput
{
    public Guid DeviceId { get; set; }

    public string Name { get; set; } = string.Empty;
}
