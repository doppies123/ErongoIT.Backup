namespace ErongoIT.Backup.Domain.Entities;

public sealed class BackupContent
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public string Sha256 { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    public string StoragePath { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; } = DateTime.UtcNow;

    private BackupContent()
    {
    }

    public BackupContent(
        string sha256,
        long sizeBytes,
        string storagePath)
    {
        if (string.IsNullOrWhiteSpace(sha256))
            throw new ArgumentException(
                "SHA-256 hash is required.",
                nameof(sha256));

        if (sizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(sizeBytes));

        if (string.IsNullOrWhiteSpace(storagePath))
            throw new ArgumentException(
                "Storage path is required.",
                nameof(storagePath));

        Sha256 = sha256.Trim().ToLowerInvariant();
        SizeBytes = sizeBytes;
        StoragePath = storagePath.Trim();
    }
}
