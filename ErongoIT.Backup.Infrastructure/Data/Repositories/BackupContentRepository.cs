using ErongoIT.Backup.Application.Persistence;
using ErongoIT.Backup.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ErongoIT.Backup.Infrastructure.Data.Repositories;

public sealed class BackupContentRepository : IBackupContentRepository
{
    private readonly BackupDbContext _db;

    public BackupContentRepository(BackupDbContext db)
    {
        _db = db;
    }

    public Task<BackupContent?> GetBySha256Async(
        string sha256,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sha256))
            throw new ArgumentException(
                "SHA-256 hash is required.",
                nameof(sha256));

        var normalized = sha256.Trim().ToLowerInvariant();

        return _db.BackupContents
            .FirstOrDefaultAsync(
                x => x.Sha256 == normalized,
                cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, BackupContent>> GetBySha256ManyAsync(
        IReadOnlyCollection<string> sha256Hashes,
        CancellationToken cancellationToken = default)
    {
        if (sha256Hashes is null || sha256Hashes.Count == 0)
            return new Dictionary<string, BackupContent>();

        var normalized = sha256Hashes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim().ToLowerInvariant())
            .Distinct()
            .ToList();

        if (normalized.Count == 0)
            return new Dictionary<string, BackupContent>();

        return await _db.BackupContents
            .AsNoTracking()
            .Where(x => normalized.Contains(x.Sha256))
            .ToDictionaryAsync(
                x => x.Sha256,
                cancellationToken);
    }

    public Task<BackupContent?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        return _db.BackupContents
            .FirstOrDefaultAsync(
                x => x.Id == id,
                cancellationToken);
    }

    public async Task AddAsync(
        BackupContent content,
        CancellationToken cancellationToken = default)
    {
        await _db.BackupContents.AddAsync(
            content,
            cancellationToken);
    }

    public Task<bool> HasReferencesAsync(
        Guid contentId,
        CancellationToken cancellationToken = default)
    {
        return _db.BackupFiles
            .AnyAsync(
                x => x.BackupContentId == contentId,
                cancellationToken);
    }

    public Task DeleteAsync(
        BackupContent content,
        CancellationToken cancellationToken = default)
    {
        _db.BackupContents.Remove(content);

        return Task.CompletedTask;
    }

    public Task SaveChangesAsync(
        CancellationToken cancellationToken = default)
    {
        return _db.SaveChangesAsync(cancellationToken);
    }
}
