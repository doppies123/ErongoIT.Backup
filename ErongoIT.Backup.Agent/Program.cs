using ErongoIT.Backup.Agent;
using ErongoIT.Backup.Agent.Api;
using ErongoIT.Backup.Agent.Configuration;
using ErongoIT.Backup.Agent.Logging;
using ErongoIT.Backup.Agent.Setup;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Options;

// ---------------------------------------------------------------
// Setup mode: ErongoIT.Backup.Agent.exe --enroll ...
// ---------------------------------------------------------------
if (args.Any(a => string.Equals(a, "--enroll", StringComparison.OrdinalIgnoreCase)))
{
    return await EnrollmentCommand.RunAsync(args);
}

// ---------------------------------------------------------------
// Agent mode (console via "dotnet run", or Windows service)
// ---------------------------------------------------------------
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args,

    // A Windows service starts in C:\Windows\System32; load
    // appsettings.json from the program folder instead.
    ContentRootPath = WindowsServiceHelpers.IsWindowsService()
        ? AppContext.BaseDirectory
        : null
});

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = AgentConfigFile.ServiceName;
});

builder.Logging.AddProvider(
    new FileLoggerProvider(AgentConfigFile.LogDirectory, "agent"));

// Enrolled PCs: C:\ProgramData\ErongoIT Backup\agent.json overrides appsettings.
var enrolledConfig = AgentConfigFile.TryLoad();

builder.Services
    .AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .PostConfigure(options => enrolledConfig?.ApplyTo(options))
    .Validate(
        options => Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out _),
        "Agent:ApiBaseUrl must be a valid absolute URI. Is this PC enrolled?")
    .Validate(
        options => options.CustomerId != Guid.Empty,
        "Agent:CustomerId is required. Is this PC enrolled?")
    .Validate(
        options => options.DeviceId != Guid.Empty,
        "Agent:DeviceId is required. Is this PC enrolled?")
    .Validate(
        options => options.GetSourceFolders().Count > 0,
        "At least one folder to back up is required (Agent:SourcePaths).")
    .Validate(
        options => options.UsesDeviceKey || !string.IsNullOrWhiteSpace(options.ApiUsername),
        "No device key (enrollment) or API username is configured.")
    .ValidateOnStart();

builder.Services.AddHttpClient<IBackupApiClient, BackupApiClient>(
    (serviceProvider, client) =>
    {
        var options = serviceProvider
            .GetRequiredService<IOptions<AgentOptions>>()
            .Value;

        client.BaseAddress = new Uri(
            options.ApiBaseUrl.TrimEnd('/') + "/");

        client.Timeout = TimeSpan.FromMinutes(30);
    });

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

await host.RunAsync();

return 0;
