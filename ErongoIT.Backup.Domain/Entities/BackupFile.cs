namespace ErongoIT.Backup.Domain.Entities;

public sealed class BackupFile
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid BackupJobId { get; private set; }

    public Guid BackupContentId { get; private set; }

    public string RelativePath { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private BackupFile()
    {
    }

    public BackupFile(
        Guid backupJobId,
        Guid backupContentId,
        string relativePath)
    {
        if (backupJobId == Guid.Empty)
            throw new ArgumentException(
                "Backup job ID is required.",
                nameof(backupJobId));

        if (backupContentId == Guid.Empty)
            throw new ArgumentException(
                "Backup content ID is required.",
                nameof(backupContentId));

        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException(
                "Relative path is required.",
                nameof(relativePath));

        BackupJobId = backupJobId;
        BackupContentId = backupContentId;
        RelativePath = relativePath.Trim();
    }
}
