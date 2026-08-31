using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ErongoIT.Backup.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ErongoIT.Backup.Web.Pages;

public sealed class LoginModel : PageModel
{
    private readonly BackupApiClient _api;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        BackupApiClient api,
        ILogger<LoginModel> logger)
    {
        _api = api;
        _logger = logger;
    }

    [BindProperty]
    [Required]
    public string Username { get; set; }
        = string.Empty;

    [BindProperty]
    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; }
        = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; private set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return LocalRedirect("/");
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        try
        {
            var login =
                await _api.LoginAsync(
                    Username,
                    Password,
                    cancellationToken);

            if (login is null ||
                string.IsNullOrWhiteSpace(
                    login.AccessToken))
            {
                ErrorMessage =
                    "Invalid username or password.";

                return Page();
            }

            var claims =
                new List<Claim>
                {
                    new(
                        ClaimTypes.Name,
                        Username)
                };

            var identity =
                new ClaimsIdentity(
                    claims,
                    CookieAuthenticationDefaults
                        .AuthenticationScheme);

            var principal =
                new ClaimsPrincipal(
                    identity);

            var properties =
                new AuthenticationProperties();

            properties.StoreTokens(
                new[]
                {
                    new AuthenticationToken
                    {
                        Name = "access_token",
                        Value = login.AccessToken
                    }
                });

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults
                    .AuthenticationScheme,
                principal,
                properties);

            if (!string.IsNullOrWhiteSpace(
                    ReturnUrl) &&
                Url.IsLocalUrl(ReturnUrl))
            {
                return LocalRedirect(
                    ReturnUrl);
            }

            return LocalRedirect("/");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Login request failed.");

            ErrorMessage =
                "Unable to sign in. Please try again.";

            return Page();
        }
    }
}
