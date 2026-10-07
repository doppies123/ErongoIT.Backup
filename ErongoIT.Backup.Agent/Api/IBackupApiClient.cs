namespace ErongoIT.Backup.Agent.Api;

public interface IBackupApiClient
{
    Task<DeviceDto?> GetDeviceAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupPlanDto>> GetBackupPlansAsync(
        Guid customerId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BackupJobDto>> GetBackupJobsAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<int> RecoverStaleJobsAsync(
        Guid deviceId,
        int timeoutMinutes,
        CancellationToken cancellationToken = default);

    Task<BackupJobDto> CreateBackupJobAsync(
        Guid customerId,
        Guid deviceId,
        Guid backupPlanId,
        int type,
        CancellationToken cancellationToken = default);

    Task StartBackupJobAsync(
        Guid backupJobId,
        CancellationToken cancellationToken = default);

    Task UpdateProgressAsync(
        Guid backupJobId,
        long bytesSelected,
        long bytesUploaded,
        CancellationToken cancellationToken = default);

    Task UploadFileAsync(
        Guid backupJobId,
        Guid customerId,
        Guid deviceId,
        string relativePath,
        Stream content,
        string fileName,
        CancellationToken cancellationToken = default,
        string? encoding = null);

    Task<ErongoIT.Backup.Agent.Backup.RegisterExistingFilesResponse> RegisterExistingFilesAsync(
        Guid backupJobId,
        Guid customerId,
        Guid deviceId,
        IReadOnlyList<ErongoIT.Backup.Agent.Backup.ExistingFileRequest> files,
        CancellationToken cancellationToken = default);

    Task CompleteBackupJobAsync(
        Guid backupJobId,
        long bytesSelected,
        long bytesUploaded,
        CancellationToken cancellationToken = default);

    Task FailBackupJobAsync(
        Guid backupJobId,
        string errorMessage,
        CancellationToken cancellationToken = default);

    // ---- File versions (incremental backup) ----

    /// <summary>Returns the SHA-256 hashes the server does not have yet.</summary>
    Task<IReadOnlyList<string>> CheckContentAsync(
        Guid deviceId,
        IReadOnlyList<ContentReferenceDto> contents,
        CancellationToken cancellationToken = default);

    Task UploadContentAsync(
        Guid deviceId,
        string sha256,
        Stream content,
        string? encoding,
        CancellationToken cancellationToken = default);

    Task<ApplyChangesResultDto> ApplyChangesAsync(
        Guid deviceId,
        Guid? backupJobId,
        IReadOnlyList<FileChangeDto> changed,
        IReadOnlyList<string> deleted,
        CancellationToken cancellationToken = default);

    Task<CurrentStatePageDto> GetSyncStateAsync(
        Guid deviceId,
        string? afterPath,
        int take,
        CancellationToken cancellationToken = default);

    Task SendHeartbeatAsync(
        Guid deviceId,
        string? agentVersion,
        CancellationToken cancellationToken = default);
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
    Guid? AssignedBackupPlanId,
    DateTime? BackupRequestedAtUtc = null);

public sealed record ContentReferenceDto(
    string Sha256,
    long SizeBytes);

public sealed record FileChangeDto(
    string Path,
    string Sha256,
    long SizeBytes,
    DateTime? LastWriteUtc);

public sealed record ApplyChangesResultDto(
    int Added,
    int Changed,
    int Unchanged,
    int Deleted,
    IReadOnlyList<string> MissingContentPaths);

public sealed record CurrentFileDto(
    string Path,
    string Sha256,
    long SizeBytes,
    DateTime? LastWriteUtc);

public sealed record CurrentStatePageDto(
    IReadOnlyList<CurrentFileDto> Files,
    string? NextAfterPath);

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
