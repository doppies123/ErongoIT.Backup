using ErongoIT.Backup.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AllowAnonymousToPage("/Login");
    options.Conventions.AllowAnonymousToPage("/Error");
});

builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.AccessDeniedPath = "/Login";

        options.Cookie.Name = "ErongoITBackupAuth";
        options.Cookie.HttpOnly = true;

        // The Docker Web container is currently accessed over HTTP.
        // SameAsRequest allows the authentication cookie to work locally
        // while still becoming Secure when HTTPS is used.
        options.Cookie.SecurePolicy =
            CookieSecurePolicy.SameAsRequest;

        options.Cookie.SameSite =
            SameSiteMode.Strict;

        options.SlidingExpiration = true;
        options.ExpireTimeSpan =
            TimeSpan.FromHours(8);
    });

builder.Services.AddAuthorization(options =>
{
    // Require authentication everywhere unless explicitly marked anonymous.
    options.FallbackPolicy =
        new AuthorizationPolicyBuilder(
                CookieAuthenticationDefaults
                    .AuthenticationScheme)
            .RequireAuthenticatedUser()
            .Build();
});

builder.Services.AddHttpContextAccessor();

builder.Services.AddHttpClient<BackupApiClient>(
    (serviceProvider, client) =>
    {
        var configuration =
            serviceProvider
                .GetRequiredService<IConfiguration>();

        var baseUrl =
            configuration["BackupApi:BaseUrl"];

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException(
                "BackupApi:BaseUrl is not configured.");
        }

        client.BaseAddress =
            new Uri(baseUrl);

        client.Timeout =
            TimeSpan.FromSeconds(30);
    });

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Do not force HTTPS while the Docker Web container
// is intentionally exposed as HTTP on localhost:5165.
// app.UseHttpsRedirection();

app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
