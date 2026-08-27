using ErongoIT.Backup.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ErongoIT.Backup.Web.Pages;

public sealed class CustomersModel : PageModel
{
    private readonly BackupApiClient _api;

    public CustomersModel(BackupApiClient api)
    {
        _api = api;
    }

    public ApiHealth? Health { get; private set; }

    public IReadOnlyList<Customer> Customers { get; private set; }
        = Array.Empty<Customer>();

    [BindProperty]
    public CreateCustomerInput CreateCustomer { get; set; }
        = new();

    [BindProperty]
    public EditCustomerInput EditCustomer { get; set; }
        = new();

    public async Task OnGetAsync(
        CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostCreateAsync(
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(CreateCustomer.Name))
        {
            ModelState.AddModelError(
                "CreateCustomer.Name",
                "Customer name is required.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _api.CreateCustomerAsync(
                CreateCustomer.Name.Trim(),
                NormalizeEmail(CreateCustomer.ContactEmail),
                cancellationToken);

            TempData["SuccessMessage"] =
                "Customer created successfully.";

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

    public async Task<IActionResult> OnPostUpdateEmailAsync(
        CancellationToken cancellationToken)
    {
        if (EditCustomer.CustomerId == Guid.Empty)
        {
            ModelState.AddModelError(
                string.Empty,
                "Customer ID is required.");
        }

        if (!ModelState.IsValid)
        {
            await LoadAsync(cancellationToken);
            return Page();
        }

        try
        {
            await _api.UpdateCustomerContactEmailAsync(
                EditCustomer.CustomerId,
                NormalizeEmail(EditCustomer.ContactEmail),
                cancellationToken);

            TempData["SuccessMessage"] =
                "Customer contact email updated successfully.";

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

    public async Task<IActionResult> OnPostActivateAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _api.ActivateCustomerAsync(
                customerId,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Customer activated successfully.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeactivateAsync(
        Guid customerId,
        CancellationToken cancellationToken)
    {
        try
        {
            await _api.DeactivateCustomerAsync(
                customerId,
                cancellationToken);

            TempData["SuccessMessage"] =
                "Customer deactivated successfully.";
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
    }

    private static string? NormalizeEmail(
        string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return null;

        return email.Trim();
    }
}

public sealed class CreateCustomerInput
{
    public string Name { get; set; } = string.Empty;

    public string? ContactEmail { get; set; }
}

public sealed class EditCustomerInput
{
    public Guid CustomerId { get; set; }

    public string? ContactEmail { get; set; }
}
