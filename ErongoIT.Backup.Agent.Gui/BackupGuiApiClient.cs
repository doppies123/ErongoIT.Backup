using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace ErongoIT.Backup.Agent.Gui;

public sealed class BackupGuiApiClient
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public BackupGuiApiClient(string baseUrl)
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(
                baseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromMinutes(30)
        };
    }

    /// <summary>Login for an enrolled PC using its device key.</summary>
    public async Task DeviceLoginAsync(
        Guid deviceId,
        string deviceKey,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _httpClient.PostAsJsonAsync(
                "api/auth/device",
                new
                {
                    deviceId,
                    deviceKey
                },
                JsonOptions,
                cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            throw new InvalidOperationException(
                "The server rejected this PC's device key. It may have been disabled " +
                "in the portal. Use Settings > Change setup to register it again.");
        }

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        var loginResponse =
            await response.Content.ReadFromJsonAsync<
                LoginResponse>(
                JsonOptions,
                cancellationToken);

        if (loginResponse is null ||
            string.IsNullOrWhiteSpace(loginResponse.AccessToken))
        {
            throw new InvalidOperationException(
                "Backup API returned an invalid login response.");
        }

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                loginResponse.TokenType,
                loginResponse.AccessToken);
    }

    public async Task LoginAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException(
                "API username was not configured.");
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException(
                "API password was not configured.");
        }

        var response =
            await _httpClient.PostAsJsonAsync(
                "api/auth/login",
                new LoginRequest(
                    username,
                    password),
                JsonOptions,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        var loginResponse =
            await response.Content.ReadFromJsonAsync<
                LoginResponse>(
                JsonOptions,
                cancellationToken);

        if (loginResponse is null ||
            string.IsNullOrWhiteSpace(
                loginResponse.AccessToken))
        {
            throw new InvalidOperationException(
                "Backup API returned an invalid login response.");
        }

        _httpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                loginResponse.TokenType,
                loginResponse.AccessToken);
    }

    public async Task<DeviceDto?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/devices/{deviceId}",
            cancellationToken);

        if (response.StatusCode ==
            System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content.ReadFromJsonAsync<
            DeviceDto>(
            JsonOptions,
            cancellationToken);
    }

    public async Task<IReadOnlyList<BackupPlanDto>>
        GetBackupPlansAsync(
            Guid customerId,
            CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/customers/{customerId}/backup-plans",
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content.ReadFromJsonAsync<
            IReadOnlyList<BackupPlanDto>>(
            JsonOptions,
            cancellationToken)
            ?? Array.Empty<BackupPlanDto>();
    }

    public async Task<IReadOnlyList<BackupJobDto>>
        GetBackupJobsAsync(
            Guid deviceId,
            CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/devices/{deviceId}/backup-jobs",
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content.ReadFromJsonAsync<
            IReadOnlyList<BackupJobDto>>(
            JsonOptions,
            cancellationToken)
            ?? Array.Empty<BackupJobDto>();
    }

    public async Task<IReadOnlyList<BackupFileDto>>
        GetBackupFilesAsync(
            Guid backupJobId,
            CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.GetAsync(
            $"api/backup-snapshots/{backupJobId}/files",
            cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content.ReadFromJsonAsync<
            IReadOnlyList<BackupFileDto>>(
            JsonOptions,
            cancellationToken)
            ?? Array.Empty<BackupFileDto>();
    }

    /// <summary>
    /// Downloads one backed-up file from the server and writes it to a
    /// local path. Written to a temporary file first, then moved into
    /// place, so an interrupted restore never leaves a half-written file.
    /// </summary>
    public async Task<long> DownloadFileAsync(
        Guid backupJobId,
        Guid backupFileId,
        string destinationFilePath,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        using var response =
            await _httpClient.GetAsync(
                $"api/backup-snapshots/{backupJobId}/files/{backupFileId}",
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        var directory =
            Path.GetDirectoryName(destinationFilePath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath =
            destinationFilePath + ".erongoit-restore.tmp";

        long written = 0;

        try
        {
            await using (var source =
                await response.Content.ReadAsStreamAsync(
                    cancellationToken))
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true))
            {
                var buffer = new byte[1024 * 1024];
                int read;
                var lastReport = DateTime.MinValue;

                while ((read = await source.ReadAsync(
                           buffer.AsMemory(0, buffer.Length),
                           cancellationToken)) > 0)
                {
                    await target.WriteAsync(
                        buffer.AsMemory(0, read),
                        cancellationToken);

                    written += read;

                    if ((DateTime.UtcNow - lastReport).TotalMilliseconds >= 250)
                    {
                        lastReport = DateTime.UtcNow;
                        progress?.Report(written);
                    }
                }

                await target.FlushAsync(cancellationToken);
            }

            File.Move(
                temporaryPath,
                destinationFilePath,
                overwrite: true);

            progress?.Report(written);

            return written;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    public async Task<BackupRestoreResultDto>
        RestoreBackupAsync(
            Guid backupJobId,
            IReadOnlyCollection<Guid> backupFileIds,
            string destinationPath,
            CancellationToken cancellationToken = default)
    {
        if (backupJobId == Guid.Empty)
        {
            throw new ArgumentException(
                "Backup job ID is required.",
                nameof(backupJobId));
        }

        if (backupFileIds is null)
        {
            throw new ArgumentNullException(
                nameof(backupFileIds));
        }

        if (string.IsNullOrWhiteSpace(
            destinationPath))
        {
            throw new ArgumentException(
                "Destination path is required.",
                nameof(destinationPath));
        }

        var selectedIds =
            backupFileIds
                .Where(x => x != Guid.Empty)
                .Distinct()
                .ToList();

        if (selectedIds.Count == 0)
        {
            throw new ArgumentException(
                "At least one backup file must be selected.",
                nameof(backupFileIds));
        }

        var response =
            await _httpClient.PostAsJsonAsync(
                $"api/backup-jobs/{backupJobId}/restore",
                new
                {
                    BackupFileIds = selectedIds,
                    DestinationPath = destinationPath
                },
                JsonOptions,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);

        return await response.Content.ReadFromJsonAsync<
                   BackupRestoreResultDto>(
                       JsonOptions,
                       cancellationToken)
               ?? throw new InvalidOperationException(
                   "Backup API returned an empty restore result.");
    }

    public async Task<BackupJobDto>
        CreateBackupJobAsync(
            Guid customerId,
            Guid deviceId,
            Guid backupPlanId,
            int type,
            CancellationToken cancellationToken = default)
    {
        var response =
            await _httpClient.PostAsJsonAsync(
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

        return await response.Content.ReadFromJsonAsync<
                   BackupJobDto>(
                       JsonOptions,
                       cancellationToken)
               ?? throw new InvalidOperationException(
                   "Backup API returned an empty backup job.");
    }

    public async Task StartBackupJobAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _httpClient.PostAsync(
                $"api/backup-jobs/{backupJobId}/start",
                null,
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
        {
            throw new ArgumentNullException(
                nameof(content));
        }

        using var form =
            new MultipartFormDataContent();

        using var streamContent =
            new StreamContent(content);

        streamContent.Headers.ContentType =
            new MediaTypeHeaderValue(
                "application/octet-stream");

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

        var response =
            await _httpClient.PostAsync(
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
        var response =
            await _httpClient.PostAsJsonAsync(
                $"api/backup-snapshots/{backupJobId}/files/existing",
                new
                {
                    customerId,
                    deviceId,
                    files
                },
                JsonOptions,
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

    public async Task CompleteBackupJobAsync(
        Guid backupJobId,
        long bytesSelected,
        long bytesUploaded,
        CancellationToken cancellationToken = default)
    {
        var response =
            await _httpClient.PostAsJsonAsync(
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
        var response =
            await _httpClient.PostAsJsonAsync(
                $"api/backup-jobs/{backupJobId}/fail",
                new
                {
                    errorMessage
                },
                JsonOptions,
                cancellationToken);

        await EnsureSuccessAsync(
            response,
            cancellationToken);
    }

    // ------------------------------------------------------------
    // Restore browser (file versions) and "Back up now"
    // ------------------------------------------------------------

    /// <summary>Asks the backup service to start a backup (within ~30 seconds).</summary>
    public async Task RequestBackupAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsync(
            $"api/devices/{deviceId}/backup-now",
            content: null,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task<FolderListingDto> BrowseAsync(
        Guid deviceId,
        string folder,
        DateTime? asOfUtc,
        bool includeDeleted,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"api/devices/{deviceId}/browse?folder={Uri.EscapeDataString(folder ?? string.Empty)}" +
            $"&includeDeleted={(includeDeleted ? "true" : "false")}" +
            AsOfQuery(asOfUtc);

        var response = await _httpClient.GetAsync(url, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<FolderListingDto>(JsonOptions, cancellationToken)
            ?? new FolderListingDto(folder ?? string.Empty, DateTime.UtcNow,
                Array.Empty<FolderItemDto>(), Array.Empty<VersionItemDto>());
    }

    public async Task<IReadOnlyList<VersionItemDto>> ResolveAsync(
        Guid deviceId,
        IReadOnlyList<string> paths,
        DateTime? asOfUtc,
        bool includeDeleted,
        CancellationToken cancellationToken = default)
    {
        var response = await _httpClient.PostAsJsonAsync(
            $"api/devices/{deviceId}/restore/resolve",
            new
            {
                paths,
                asOf = asOfUtc?.ToUniversalTime(),
                includeDeleted
            },
            JsonOptions,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<VersionItemDto>>(JsonOptions, cancellationToken)
            ?? Array.Empty<VersionItemDto>();
    }

    public async Task<IReadOnlyList<VersionItemDto>> SearchAsync(
        Guid deviceId,
        string query,
        DateTime? asOfUtc,
        bool includeDeleted,
        CancellationToken cancellationToken = default)
    {
        var url =
            $"api/devices/{deviceId}/search?q={Uri.EscapeDataString(query)}" +
            $"&includeDeleted={(includeDeleted ? "true" : "false")}&take=1000" +
            AsOfQuery(asOfUtc);

        var response = await _httpClient.GetAsync(url, cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        return await response.Content.ReadFromJsonAsync<IReadOnlyList<VersionItemDto>>(JsonOptions, cancellationToken)
            ?? Array.Empty<VersionItemDto>();
    }

    /// <summary>Downloads one file version to a local path (via a temporary file).</summary>
    public async Task<long> DownloadVersionAsync(
        Guid versionId,
        string destinationFilePath,
        CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync(
            $"api/file-versions/{versionId}/content",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        await EnsureSuccessAsync(response, cancellationToken);

        var directory = Path.GetDirectoryName(destinationFilePath);

        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var temporaryPath = destinationFilePath + ".erongoit-restore.tmp";
        long written = 0;

        try
        {
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var target = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 1024,
                useAsync: true))
            {
                var buffer = new byte[1024 * 1024];
                int read;

                while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    written += read;
                }

                await target.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, destinationFilePath, overwrite: true);

            return written;
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }

    private static string AsOfQuery(DateTime? asOfUtc) =>
        asOfUtc is null
            ? string.Empty
            : "&asOf=" + Uri.EscapeDataString(
                asOfUtc.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ",
                    System.Globalization.CultureInfo.InvariantCulture));

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body =
            await response.Content.ReadAsStringAsync(
                cancellationToken);

        throw new HttpRequestException(
            $"Backup API request failed with HTTP " +
            $"{(int)response.StatusCode}: {body}");
    }

    private sealed record LoginRequest(
        string Username,
        string Password);

    private sealed record LoginResponse(
        string AccessToken,
        string TokenType);
}

public sealed record DeviceDto(
    Guid Id,
    Guid CustomerId,
    string Name,
    string? Hostname,
    string? OperatingSystem,
    string? AgentVersion,
    DateTime? LastSeenAtUtc,
    bool IsActive,
    Guid? AssignedBackupPlanId);

public sealed record BackupPlanDto(
    Guid Id,
    Guid CustomerId,
    string Name,
    int ScheduleType,
    int IntervalMinutes,
    int ScheduleTimeMinutes,
    int ScheduleDayOfWeek,
    int RetentionDays,
    bool IsEnabled);

public sealed record BackupJobDto(
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

public sealed record BackupFileDto(
    Guid Id,
    Guid BackupJobId,
    Guid BackupContentId,
    string RelativePath,
    DateTime CreatedAtUtc);

public sealed record BackupRestoreResultDto(
    Guid BackupJobId,
    string DestinationPath,
    int FilesRestored,
    long BytesRestored);

public sealed record FolderItemDto(
    string Name,
    string Path,
    int FileCount,
    int FolderCount,
    long SizeBytes,
    DateTime? LastWriteUtc,
    bool Deleted);

public sealed record VersionItemDto(
    Guid VersionId,
    string Name,
    string Path,
    long SizeBytes,
    DateTime? LastWriteUtc,
    DateTime BackedUpUtc,
    bool Deleted);

public sealed record FolderListingDto(
    string Folder,
    DateTime AsOfUtc,
    IReadOnlyList<FolderItemDto> Folders,
    IReadOnlyList<VersionItemDto> Files);
