using System.Net.Http.Json;
using System.Text.Json;

namespace ErongoIT.Backup.Web.Services;

public sealed class BackupApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BackupApiClient> _logger;

    public BackupApiClient(
        HttpClient httpClient,
        ILogger<BackupApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ApiHealth?> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<ApiHealth>(
                "api/health",
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Unable to retrieve backup API health.");

            return null;
        }
    }

    public async Task<IReadOnlyList<Customer>> GetCustomersAsync(
        CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<
            IReadOnlyList<Customer>>(
                "api/customers",
                cancellationToken)
            ?? Array.Empty<Customer>();
    }

    public async Task<Customer> CreateCustomerAsync(
        string name,
        string? contactEmail,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/customers",
            new CreateCustomerApiRequest(
                name,
                contactEmail),
            cancellationToken);

        await EnsureSuccessAsync(response);

        var customer =
            await response.Content.ReadFromJsonAsync<Customer>(
                cancellationToken);

        return customer
            ?? throw new InvalidOperationException(
                "The backup API did not return the created customer.");
    }

    public async Task UpdateCustomerContactEmailAsync(
        Guid customerId,
        string? contactEmail,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"api/customers/{customerId}/contact-email",
            new UpdateContactEmailApiRequest(contactEmail),
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task ActivateCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/customers/{customerId}/activate",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task DeactivateCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/customers/{customerId}/deactivate",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<
            IReadOnlyList<Device>>(
                $"api/customers/{customerId}/devices",
                cancellationToken)
            ?? Array.Empty<Device>();
    }

    public async Task<Device> CreateDeviceAsync(
        Guid customerId,
        string name,
        string? hostname,
        string? operatingSystem,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/customers/{customerId}/devices",
            new CreateDeviceApiRequest(
                name,
                hostname,
                operatingSystem),
            cancellationToken);

        await EnsureSuccessAsync(response);

        var device =
            await response.Content.ReadFromJsonAsync<Device>(
                cancellationToken);

        return device
            ?? throw new InvalidOperationException(
                "The backup API did not return the created device.");
    }

    public async Task RenameDeviceAsync(
        Guid deviceId,
        string name,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"api/devices/{deviceId}/name",
            new RenameDeviceApiRequest(name),
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task RecordHeartbeatAsync(
        Guid deviceId,
        string? agentVersion,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/devices/{deviceId}/heartbeat",
            new HeartbeatApiRequest(agentVersion),
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task ActivateDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/devices/{deviceId}/activate",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task DeactivateDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/devices/{deviceId}/deactivate",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task<IReadOnlyList<BackupPlan>> GetBackupPlansAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<
            IReadOnlyList<BackupPlan>>(
                $"api/customers/{customerId}/backup-plans",
                cancellationToken)
            ?? Array.Empty<BackupPlan>();
    }

    public async Task<BackupPlan> CreateBackupPlanAsync(
        Guid customerId,
        string name,
        int scheduleType,
        int intervalMinutes,
        int scheduleTimeMinutes,
        int scheduleDayOfWeek,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/customers/{customerId}/backup-plans",
            new CreateBackupPlanApiRequest(
                name,
                scheduleType,
                intervalMinutes,
                scheduleTimeMinutes,
                scheduleDayOfWeek,
                retentionDays),
            cancellationToken);

        await EnsureSuccessAsync(response);

        var plan =
            await response.Content.ReadFromJsonAsync<BackupPlan>(
                cancellationToken);

        return plan
            ?? throw new InvalidOperationException(
                "The backup API did not return the created backup plan.");
    }

    public async Task UpdateBackupPlanScheduleAsync(
        Guid backupPlanId,
        int scheduleType,
        int intervalMinutes,
        int scheduleTimeMinutes,
        int scheduleDayOfWeek,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PutAsJsonAsync(
            $"api/backup-plans/{backupPlanId}/schedule",
            new UpdateBackupPlanScheduleApiRequest(
                scheduleType,
                intervalMinutes,
                scheduleTimeMinutes,
                scheduleDayOfWeek,
                retentionDays),
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task EnableBackupPlanAsync(
        Guid backupPlanId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/backup-plans/{backupPlanId}/enable",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task DisableBackupPlanAsync(
        Guid backupPlanId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/backup-plans/{backupPlanId}/disable",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task<IReadOnlyList<BackupJob>> GetBackupJobsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        return await _httpClient.GetFromJsonAsync<
            IReadOnlyList<BackupJob>>(
                $"api/devices/{deviceId}/backup-jobs",
                cancellationToken)
            ?? Array.Empty<BackupJob>();
    }

    public async Task<BackupJob> CreateBackupJobAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupPlanId,
        int type,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine("========== WEB -> API CREATE JOB ==========");
        Console.WriteLine($"POST: api/customers/{customerId}/backup-jobs");
        Console.WriteLine($"DeviceId: {deviceId}");
        Console.WriteLine($"BackupPlanId: {backupPlanId}");
        Console.WriteLine($"Type: {type}");

        var response = await _httpClient.PostAsJsonAsync(
            $"api/customers/{customerId}/backup-jobs",
            new CreateBackupJobApiRequest(
                deviceId,
                backupPlanId,
                type),
            cancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(
            cancellationToken);

        Console.WriteLine($"API STATUS: {(int)response.StatusCode} {response.StatusCode}");
        Console.WriteLine($"API RESPONSE: {responseBody}");

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Backup API returned HTTP {(int)response.StatusCode}: {responseBody}");
        }

        var job =
            System.Text.Json.JsonSerializer.Deserialize<BackupJob>(
                responseBody,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

        return job
            ?? throw new InvalidOperationException(
                "The backup API did not return the created backup job.");
    }

    public async Task StartBackupJobAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/backup-jobs/{backupJobId}/start",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task CompleteBackupJobAsync(
        Guid backupJobId,
        long bytesSelected,
        long bytesUploaded,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/backup-jobs/{backupJobId}/complete",
            new CompleteBackupJobApiRequest(
                bytesSelected,
                bytesUploaded),
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task FailBackupJobAsync(
        Guid backupJobId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/backup-jobs/{backupJobId}/fail",
            new FailBackupJobApiRequest(errorMessage),
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task CancelBackupJobAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/backup-jobs/{backupJobId}/cancel",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task ClearBackupJobHistoryAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/devices/{deviceId}/backup-jobs/clear-history",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response);
    }


    public async Task<int> RecoverStaleBackupJobsAsync(
        Guid deviceId,
        int timeoutMinutes = 30,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/devices/{deviceId}/backup-jobs/recover-stale?timeoutMinutes={timeoutMinutes}",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response);

        var result =
            await response.Content.ReadFromJsonAsync<RecoverStaleJobsResult>(
                cancellationToken);

        return result?.Recovered ?? 0;
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message = await ReadErrorMessageAsync(response);

        throw new InvalidOperationException(message);
    }

    private static async Task<string> ReadErrorMessageAsync(
        HttpResponseMessage response)
    {
        try
        {
            var error =
                await response.Content.ReadFromJsonAsync<ApiError>();

            if (!string.IsNullOrWhiteSpace(error?.Error))
                return error.Error;
        }
        catch (JsonException)
        {
        }

        return $"Backup API request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
    }
}

public sealed record CreateCustomerApiRequest(
    string Name,
    string? ContactEmail);

public sealed record UpdateContactEmailApiRequest(
    string? ContactEmail);

public sealed record CreateDeviceApiRequest(
    string Name,
    string? Hostname,
    string? OperatingSystem);

public sealed record RenameDeviceApiRequest(
    string Name);

public sealed record HeartbeatApiRequest(
    string? AgentVersion);

public sealed record CreateBackupPlanApiRequest(
    string Name,
    int ScheduleType,
    int IntervalMinutes,
    int ScheduleTimeMinutes,
    int ScheduleDayOfWeek,
    int RetentionDays);

public sealed record UpdateBackupPlanScheduleApiRequest(
    int ScheduleType,
    int IntervalMinutes,
    int ScheduleTimeMinutes,
    int ScheduleDayOfWeek,
    int RetentionDays);

public sealed record CreateBackupJobApiRequest(
    Guid DeviceId,
    Guid BackupPlanId,
    int Type);

public sealed record CompleteBackupJobApiRequest(
    long BytesSelected,
    long BytesUploaded);

public sealed record FailBackupJobApiRequest(
    string ErrorMessage);

public sealed record RecoverStaleJobsResult(
    Guid DeviceId,
    int TimeoutMinutes,
    int Recovered);

public sealed record ApiError(
    string? Error);

public sealed record ApiHealth(
    string Status,
    string Database,
    DateTime TimestampUtc);

public sealed record Customer(
    Guid Id,
    string Name,
    string? ContactEmail,
    bool IsActive);

public sealed record Device(
    Guid Id,
    Guid CustomerId,
    string Name,
    string? Hostname,
    string? OperatingSystem,
    string? AgentVersion,
    DateTime? LastSeenAtUtc,
    bool IsActive);

public sealed record BackupPlan(
    Guid Id,
    Guid CustomerId,
    string Name,
    int ScheduleType,
    int IntervalMinutes,
    int ScheduleTimeMinutes,
    int ScheduleDayOfWeek,
    int RetentionDays,
    bool IsEnabled);

public sealed record BackupJob(
    Guid Id,
    Guid CustomerId,
    Guid DeviceId,
    Guid BackupPlanId,
    int Type,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    long BytesSelected,
    long BytesUploaded,
    string Status,
    string? ErrorMessage);
