using ErongoIT.Backup.Agent;
using ErongoIT.Backup.Agent.Api;
using ErongoIT.Backup.Agent.Configuration;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services
    .AddOptions<AgentOptions>()
    .Bind(builder.Configuration.GetSection(AgentOptions.SectionName))
    .Validate(
        options => Uri.TryCreate(
            options.ApiBaseUrl,
            UriKind.Absolute,
            out _),
        "Agent:ApiBaseUrl must be a valid absolute URI.")
    .Validate(
        options => options.CustomerId != Guid.Empty,
        "Agent:CustomerId is required.")
    .Validate(
        options => options.DeviceId != Guid.Empty,
        "Agent:DeviceId is required.")
    .Validate(
        options => !string.IsNullOrWhiteSpace(options.SourcePath),
        "Agent:SourcePath is required.")
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

host.Run();
