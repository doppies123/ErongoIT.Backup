using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace ErongoIT.Backup.Web.Services;

public sealed class BackupApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<BackupApiClient> _logger;

    public BackupApiClient(
        HttpClient httpClient,
        IHttpContextAccessor httpContextAccessor,
        ILogger<BackupApiClient> logger)
    {
        _httpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    public async Task<LoginResponse?> LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            "api/auth/login",
            new LoginRequest(username, password),
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<LoginResponse>(
            cancellationToken);
    }

    public async Task<ApiHealth?> GetHealthAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var request =
                await CreateRequestAsync(
                    HttpMethod.Get,
                    "api/health");

            using var response =
                await _httpClient.SendAsync(
                    request,
                    cancellationToken);

            if (!response.IsSuccessStatusCode)
                return null;

            return await response.Content.ReadFromJsonAsync<ApiHealth>(
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
        using var request =
            await CreateRequestAsync(
                HttpMethod.Get,
                "api/customers");

        using var response =
            await _httpClient.SendAsync(
                request,
                cancellationToken);

        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<
            IReadOnlyList<Customer>>(
                cancellationToken)
            ?? Array.Empty<Customer>();
    }

    public async Task<Customer> CreateCustomerAsync(
        string name,
        string? contactEmail,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                "api/customers",
                new CreateCustomerApiRequest(
                    name,
                    contactEmail));

        using var response =
            await _httpClient.SendAsync(
                request,
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
        using var request =
            await CreateRequestAsync(
                HttpMethod.Put,
                $"api/customers/{customerId}/contact-email",
                new UpdateContactEmailApiRequest(contactEmail));

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task ActivateCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/customers/{customerId}/activate");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task DeactivateCustomerAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/customers/{customerId}/deactivate");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task<IReadOnlyList<Device>> GetDevicesAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Get,
                $"api/customers/{customerId}/devices");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<
            IReadOnlyList<Device>>(cancellationToken)
            ?? Array.Empty<Device>();
    }

    public async Task<Device> CreateDeviceAsync(
        Guid customerId,
        string name,
        string? hostname,
        string? operatingSystem,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/customers/{customerId}/devices",
                new CreateDeviceApiRequest(
                    name,
                    hostname,
                    operatingSystem));

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

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
        using var request =
            await CreateRequestAsync(
                HttpMethod.Put,
                $"api/devices/{deviceId}/name",
                new RenameDeviceApiRequest(name));

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task RecordHeartbeatAsync(
        Guid deviceId,
        string? agentVersion,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/devices/{deviceId}/heartbeat",
                new HeartbeatApiRequest(agentVersion));

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task ActivateDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/devices/{deviceId}/activate");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task DeactivateDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/devices/{deviceId}/deactivate");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task<IReadOnlyList<BackupPlan>> GetBackupPlansAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Get,
                $"api/customers/{customerId}/backup-plans");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<
            IReadOnlyList<BackupPlan>>(cancellationToken)
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
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/customers/{customerId}/backup-plans",
                new CreateBackupPlanApiRequest(
                    name,
                    scheduleType,
                    intervalMinutes,
                    scheduleTimeMinutes,
                    scheduleDayOfWeek,
                    retentionDays));

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

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
        using var request =
            await CreateRequestAsync(
                HttpMethod.Put,
                $"api/backup-plans/{backupPlanId}/schedule",
                new UpdateBackupPlanScheduleApiRequest(
                    scheduleType,
                    intervalMinutes,
                    scheduleTimeMinutes,
                    scheduleDayOfWeek,
                    retentionDays));

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task EnableBackupPlanAsync(
        Guid backupPlanId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/backup-plans/{backupPlanId}/enable");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task DisableBackupPlanAsync(
        Guid backupPlanId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/backup-plans/{backupPlanId}/disable");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task<IReadOnlyList<BackupJob>> GetBackupJobsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Get,
                $"api/devices/{deviceId}/backup-jobs");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);

        return await response.Content.ReadFromJsonAsync<
            IReadOnlyList<BackupJob>>(cancellationToken)
            ?? Array.Empty<BackupJob>();
    }

    public async Task<BackupJob> CreateBackupJobAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupPlanId,
        int type,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/customers/{customerId}/backup-jobs",
                new CreateBackupJobApiRequest(
                    deviceId,
                    backupPlanId,
                    type));

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);

        var job =
            await response.Content.ReadFromJsonAsync<BackupJob>(
                cancellationToken);

        return job
            ?? throw new InvalidOperationException(
                "The backup API did not return the created backup job.");
    }

    public async Task StartBackupJobAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/backup-jobs/{backupJobId}/start");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task CompleteBackupJobAsync(
        Guid backupJobId,
        long bytesSelected,
        long bytesUploaded,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/backup-jobs/{backupJobId}/complete",
                new CompleteBackupJobApiRequest(
                    bytesSelected,
                    bytesUploaded));

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task FailBackupJobAsync(
        Guid backupJobId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/backup-jobs/{backupJobId}/fail",
                new FailBackupJobApiRequest(errorMessage));

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task CancelBackupJobAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/backup-jobs/{backupJobId}/cancel");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task ClearBackupJobHistoryAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/devices/{deviceId}/backup-jobs/clear-history");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);
    }

    public async Task<int> RecoverStaleBackupJobsAsync(
        Guid deviceId,
        int timeoutMinutes = 30,
        CancellationToken cancellationToken = default)
    {
        using var request =
            await CreateRequestAsync(
                HttpMethod.Post,
                $"api/devices/{deviceId}/backup-jobs/recover-stale?timeoutMinutes={timeoutMinutes}");

        using var response =
            await _httpClient.SendAsync(request, cancellationToken);

        await EnsureSuccessAsync(response);

        var result =
            await response.Content.ReadFromJsonAsync<
                RecoverStaleJobsResult>(cancellationToken);

        return result?.Recovered ?? 0;
    }

    private async Task<HttpRequestMessage> CreateRequestAsync(
        HttpMethod method,
        string requestUri,
        object? content = null)
    {
        var request =
            new HttpRequestMessage(
                method,
                requestUri);

        var token =
            await GetAccessTokenAsync();

        var authenticated =
            _httpContextAccessor
                .HttpContext?
                .User
                .Identity?
                .IsAuthenticated
            ?? false;

        _logger.LogInformation(
            "Backup API request {Method} {Uri}. Web authenticated: {Authenticated}. Access token available: {HasToken}.",
            method,
            requestUri,
            authenticated,
            !string.IsNullOrWhiteSpace(token));

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    token);
        }

        if (content is not null)
        {
            request.Content =
                JsonContent.Create(content);
        }

        return request;
    }

    private async Task<string?> GetAccessTokenAsync()
    {
        var httpContext =
            _httpContextAccessor.HttpContext;

        if (httpContext is null)
            return null;

        return await httpContext.GetTokenAsync(
            "access_token");
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;

        var message =
            await ReadErrorMessageAsync(response);

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

        return
            $"Backup API request failed with HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).";
    }
}

public sealed record LoginRequest(
    string Username,
    string Password);

public sealed record LoginResponse(
    string AccessToken,
    string TokenType);

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
