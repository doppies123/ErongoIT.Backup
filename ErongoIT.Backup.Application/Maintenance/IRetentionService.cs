namespace ErongoIT.Backup.Application.Maintenance;

/// <summary>
/// Applies each backup plan's retention period: removes backup jobs older
/// than the plan allows (always keeping the newest completed backup per
/// device) and then deletes stored content no longer used by any backup.
/// </summary>
public interface IRetentionService
{
    Task<RetentionReport> RunAsync(
        bool dryRun,
        CancellationToken cancellationToken = default);
}

public sealed record RetentionReport(
    bool DryRun,
    DateTime RanAtUtc,
    int JobsDeleted,
    int FilesDeleted,
    int ContentsDeleted,
    long BytesFreed,
    bool ContentCleanupSkipped,
    IReadOnlyList<PlanRetentionSummary> Plans);

public sealed record PlanRetentionSummary(
    Guid PlanId,
    string PlanName,
    int RetentionDays,
    int JobsDeleted,
    int JobsKept);
