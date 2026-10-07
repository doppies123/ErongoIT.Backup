using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ErongoIT.Backup.Agent.Configuration;
using Microsoft.Extensions.Options;

namespace ErongoIT.Backup.Agent.Api;

public sealed class BackupApiClient : IBackupApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BackupApiClient> _logger;
    private readonly AgentOptions _options;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private string? _accessToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _authenticationLock = new(1, 1);

    public BackupApiClient(
        HttpClient httpClient,
        ILogger<BackupApiClient> logger,
        IOptions<AgentOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<DeviceDto?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            HttpMethod.Get,
            $"api/devices/{deviceId}",
            cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<DeviceDto>(
            JsonOptions,
            cancellationToken);
    }

    public async Task<IReadOnlyList<BackupPlanDto>> GetBackupPlansAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var response = await SendAsync(
            HttpMethod.Get,
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
        var response = await SendAsync(
            HttpMethod.Get,
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

        var response = await SendAsync(
            HttpMethod.Post,
            $"api/devices/{deviceId}/backup-jobs/recover-stale" +
            $"?timeoutMinutes={timeoutMinutes}",
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
        var response = await SendJsonAsync(
            HttpMethod.Post,
            $"api/customers/{customerId}/backup-jobs",
            new
            {
                deviceId,
                backupPlanId,
                type
            },
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
        var response = await SendAsync(
            HttpMethod.Post,
            $"api/backup-jobs/{backupJobId}/start",
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
        CancellationToken cancellationToken = default,
        string? encoding = null)
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
            $"&relativePath={Uri.EscapeDataString(relativePath)}" +
            (string.IsNullOrWhiteSpace(encoding)
                ? string.Empty
                : $"&encoding={Uri.EscapeDataString(encoding)}");

        var response = await SendAsync(
            HttpMethod.Post,
            url,
            form,
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    public async Task<ErongoIT.Backup.Agent.Backup.RegisterExistingFilesResponse> RegisterExistingFilesAsync(
        Guid backupJobId,
        Guid customerId,
        Guid deviceId,
        IReadOnlyList<ErongoIT.Backup.Agent.Backup.ExistingFileRequest> files,
        CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync(
            HttpMethod.Post,
            $"api/backup-snapshots/{backupJobId}/files/existing",
            new
            {
                customerId,
                deviceId,
                files
            },
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content
                   .ReadFromJsonAsync<ErongoIT.Backup.Agent.Backup.RegisterExistingFilesResponse>(
                       JsonOptions,
                       cancellationToken)
               ?? throw new InvalidOperationException(
                   "The backup API returned an empty response when checking existing files.");
    }

    public async Task UpdateProgressAsync(
        Guid backupJobId,
        long bytesSelected,
        long bytesUploaded,
        CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync(
            HttpMethod.Post,
            $"api/backup-jobs/{backupJobId}/progress",
            new
            {
                bytesSelected,
                bytesUploaded
            },
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
        var response = await SendJsonAsync(
            HttpMethod.Post,
            $"api/backup-jobs/{backupJobId}/complete",
            new
            {
                bytesSelected,
                bytesUploaded
            },
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
        var response = await SendJsonAsync(
            HttpMethod.Post,
            $"api/backup-jobs/{backupJobId}/fail",
            new
            {
                errorMessage
            },
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Failed to mark backup job {BackupJobId} as failed. HTTP {StatusCode}.",
                backupJobId,
                (int)response.StatusCode);
        }
    }

    public async Task<IReadOnlyList<string>> CheckContentAsync(
        Guid deviceId,
        IReadOnlyList<ContentReferenceDto> contents,
        CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync(
            HttpMethod.Post,
            $"api/devices/{deviceId}/sync/check-content",
            new { contents },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var result = await response.Content.ReadFromJsonAsync<CheckContentResponse>(
            JsonOptions,
            cancellationToken);

        return result?.Missing ?? Array.Empty<string>();
    }

    public async Task UploadContentAsync(
        Guid deviceId,
        string sha256,
        Stream content,
        string? encoding,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        using var form = new MultipartFormDataContent();
        using var streamContent = new StreamContent(content);

        form.Add(streamContent, "file", sha256);

        var url =
            $"api/devices/{deviceId}/sync/content?sha256={Uri.EscapeDataString(sha256)}" +
            (string.IsNullOrWhiteSpace(encoding)
                ? string.Empty
                : $"&encoding={Uri.EscapeDataString(encoding)}");

        var response = await SendAsync(HttpMethod.Post, url, form, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<ApplyChangesResultDto> ApplyChangesAsync(
        Guid deviceId,
        Guid? backupJobId,
        IReadOnlyList<FileChangeDto> changed,
        IReadOnlyList<string> deleted,
        CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync(
            HttpMethod.Post,
            $"api/devices/{deviceId}/sync/changes",
            new { backupJobId, changed, deleted },
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<ApplyChangesResultDto>(
                   JsonOptions,
                   cancellationToken)
               ?? throw new InvalidOperationException(
                   "The backup API returned an empty response when sending changes.");
    }

    public async Task<CurrentStatePageDto> GetSyncStateAsync(
        Guid deviceId,
        string? afterPath,
        int take,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/devices/{deviceId}/sync/state?take={take}" +
                  (string.IsNullOrEmpty(afterPath)
                      ? string.Empty
                      : $"&after={Uri.EscapeDataString(afterPath)}");

        var response = await SendAsync(HttpMethod.Get, url, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<CurrentStatePageDto>(
                   JsonOptions,
                   cancellationToken)
               ?? new CurrentStatePageDto(Array.Empty<CurrentFileDto>(), null);
    }

    public async Task SendHeartbeatAsync(
        Guid deviceId,
        string? agentVersion,
        CancellationToken cancellationToken = default)
    {
        var response = await SendJsonAsync(
            HttpMethod.Post,
            $"api/devices/{deviceId}/heartbeat",
            new
            {
                agentVersion
            },
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string requestUri,
        CancellationToken cancellationToken)
    {
        return await SendAsync(
            method,
            requestUri,
            content: null,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string requestUri,
        HttpContent? content,
        CancellationToken cancellationToken)
    {
        var token = await GetAccessTokenAsync(cancellationToken);

        using var request = new HttpRequestMessage(
            method,
            requestUri);

        request.Content = content;

        request.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        var response = await _httpClient.SendAsync(
            request,
            cancellationToken);

        if (response.StatusCode != System.Net.HttpStatusCode.Unauthorized)
            return response;

        response.Dispose();

        _accessToken = null;
        _tokenExpiresAt = DateTimeOffset.MinValue;

        token = await GetAccessTokenAsync(cancellationToken);

        using var retryRequest = new HttpRequestMessage(
            method,
            requestUri);

        retryRequest.Content = content;

        retryRequest.Headers.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                token);

        return await _httpClient.SendAsync(
            retryRequest,
            cancellationToken);
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        HttpMethod method,
        string requestUri,
        object body,
        CancellationToken cancellationToken)
    {
        var json = JsonContent.Create(
            body,
            options: JsonOptions);

        return await SendAsync(
            method,
            requestUri,
            json,
            cancellationToken);
    }

    private async Task<string> GetAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_accessToken) &&
            DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _accessToken;
        }

        await _authenticationLock.WaitAsync(cancellationToken);

        try
        {
            if (!string.IsNullOrWhiteSpace(_accessToken) &&
                DateTimeOffset.UtcNow < _tokenExpiresAt)
            {
                return _accessToken;
            }

            HttpResponseMessage response;

            if (_options.UsesDeviceKey)
            {
                // Enrolled PC: log in with the device's own key.
                response = await _httpClient.PostAsJsonAsync(
                    "api/auth/device",
                    new
                    {
                        deviceId = _options.DeviceId,
                        deviceKey = _options.DeviceKey
                    },
                    JsonOptions,
                    cancellationToken);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    throw new InvalidOperationException(
                        "The server rejected this device's key. The device may have been " +
                        "disabled or its key revoked. Re-enroll this PC.");
                }
            }
            else
            {
                // Developer fallback: admin username/password.
                if (string.IsNullOrWhiteSpace(_options.ApiUsername) ||
                    string.IsNullOrWhiteSpace(_options.ApiPassword))
                {
                    throw new InvalidOperationException(
                        "This PC is not enrolled and no API username/password is configured.");
                }

                response = await _httpClient.PostAsJsonAsync(
                    "api/auth/login",
                    new
                    {
                        username = _options.ApiUsername,
                        password = _options.ApiPassword
                    },
                    JsonOptions,
                    cancellationToken);
            }

            if (!response.IsSuccessStatusCode)
            {
                await EnsureSuccessAsync(
                    response,
                    cancellationToken);
            }

            var login =
                await response.Content.ReadFromJsonAsync<LoginResponse>(
                    JsonOptions,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "The backup API returned an empty login response.");

            if (string.IsNullOrWhiteSpace(login.AccessToken))
            {
                throw new InvalidOperationException(
                    "The backup API returned an empty access token.");
            }

            _accessToken = login.AccessToken;

            // The API issues tokens for 8 hours.
            // Renew five minutes before expiry.
            _tokenExpiresAt =
                DateTimeOffset.UtcNow.AddHours(8).AddMinutes(-5);

            _logger.LogInformation(
                "Authenticated with the backup API as {Identity}.",
                _options.UsesDeviceKey
                    ? $"device {_options.DeviceId}"
                    : _options.ApiUsername);

            return _accessToken;
        }
        finally
        {
            _authenticationLock.Release();
        }
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

    private sealed record CheckContentResponse(
        IReadOnlyList<string> Missing);

    private sealed record LoginResponse(
        string AccessToken,
        string TokenType);

    private sealed record RecoverStaleJobsResponse(
        Guid DeviceId,
        int TimeoutMinutes,
        int Recovered);
}
