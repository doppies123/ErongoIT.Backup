using System.Net.Http.Json;
using System.Text.Json;

namespace ErongoIT.Backup.Agent.Api;

public sealed class BackupApiClient : IBackupApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BackupApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public BackupApiClient(
        HttpClient httpClient,
        ILogger<BackupApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<DeviceDto?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/devices/{deviceId}",
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content.ReadFromJsonAsync<DeviceDto>(
            JsonOptions,
            cancellationToken);
    }


    public async Task<IReadOnlyList<BackupPlanDto>> GetBackupPlansAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/customers/{customerId}/backup-plans",
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<
            IReadOnlyList<BackupPlanDto>>(
                JsonOptions,
                cancellationToken)
            ?? Array.Empty<BackupPlanDto>();
    }

    public async Task<IReadOnlyList<BackupJobDto>> GetBackupJobsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/devices/{deviceId}/backup-jobs",
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<
            IReadOnlyList<BackupJobDto>>(
                JsonOptions,
                cancellationToken)
            ?? Array.Empty<BackupJobDto>();
    }

    public async Task<int> RecoverStaleJobsAsync(
        Guid deviceId,
        int timeoutMinutes,
        CancellationToken cancellationToken = default)
    {
        if (timeoutMinutes <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(timeoutMinutes));

        var response = await _httpClient.PostAsync(
            $"api/devices/{deviceId}/backup-jobs/recover-stale" +
            $"?timeoutMinutes={timeoutMinutes}",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        var result =
            await response.Content.ReadFromJsonAsync<RecoverStaleJobsResponse>(
                JsonOptions,
                cancellationToken);

        return result?.Recovered ?? 0;
    }

    public async Task<BackupJobDto> CreateBackupJobAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupPlanId,
        int type,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/customers/{customerId}/backup-jobs",
            new
            {
                deviceId,
                backupPlanId,
                type
            },
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        var job =
            await response.Content.ReadFromJsonAsync<BackupJobDto>(
                JsonOptions,
                cancellationToken);

        return job
            ?? throw new InvalidOperationException(
                "The backup API returned an empty backup job response.");
    }

    public async Task StartBackupJobAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/backup-jobs/{backupJobId}/start",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public async Task UploadFileAsync(
        Guid backupJobId,
        Guid customerId,
        Guid deviceId,
        string relativePath,
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default)
    {
        if (content is null)
            throw new ArgumentNullException(nameof(content));

        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(content);

        form.Add(
            streamContent,
            "file",
            fileName);

        var url =
            $"api/backup-snapshots/{backupJobId}/files" +
            $"?customerId={Uri.EscapeDataString(customerId.ToString())}" +
            $"&deviceId={Uri.EscapeDataString(deviceId.ToString())}" +
            $"&relativePath={Uri.EscapeDataString(relativePath)}";

        var response = await _httpClient.PostAsync(
            url,
            form,
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public async Task UpdateProgressAsync(
        Guid backupJobId,
        long bytesSelected,
        long bytesUploaded,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/backup-jobs/{backupJobId}/progress",
            new
            {
                bytesSelected,
                bytesUploaded
            },
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }


    public async Task CompleteBackupJobAsync(
        Guid backupJobId,
        long bytesSelected,
        long bytesUploaded,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/backup-jobs/{backupJobId}/complete",
            new
            {
                bytesSelected,
                bytesUploaded
            },
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public async Task FailBackupJobAsync(
        Guid backupJobId,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/backup-jobs/{backupJobId}/fail",
            new
            {
                errorMessage
            },
            JsonOptions,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Failed to mark backup job {BackupJobId} as failed. HTTP {StatusCode}.",
                backupJobId,
                (int)response.StatusCode);
        }
    }

    public async Task SendHeartbeatAsync(
        Guid deviceId,
        string? agentVersion,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/devices/{deviceId}/heartbeat",
            new
            {
                agentVersion
            },
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync(
            cancellationToken);

        var requestUrl =
            response.RequestMessage?.RequestUri?.ToString()
            ?? "<unknown URL>";

        throw new HttpRequestException(
            $"Backup API request failed with HTTP {(int)response.StatusCode} " +
            $"({response.StatusCode}) at {requestUrl}: {body}");
    }

    private sealed record RecoverStaleJobsResponse(
        Guid DeviceId,
        int TimeoutMinutes,
        int Recovered);
}
