using ErongoIT.Backup.Domain.Enums;

namespace ErongoIT.Backup.Domain.Entities;

public sealed class BackupJob
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid CustomerId { get; private set; }

    public Guid DeviceId { get; private set; }

    public Guid BackupPlanId { get; private set; }

    public BackupType Type { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public long BytesSelected { get; private set; }

    public long BytesUploaded { get; private set; }

    public string Status { get; private set; } = "Pending";

    public string? ErrorMessage { get; private set; }

    private BackupJob()
    {
    }

    public BackupJob(
        Guid customerId,
        Guid deviceId,
        Guid backupPlanId,
        BackupType type)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException(
                "Customer ID is required.",
                nameof(customerId));

        if (deviceId == Guid.Empty)
            throw new ArgumentException(
                "Device ID is required.",
                nameof(deviceId));

        if (backupPlanId == Guid.Empty)
            throw new ArgumentException(
                "Backup plan ID is required.",
                nameof(backupPlanId));

        CustomerId = customerId;
        DeviceId = deviceId;
        BackupPlanId = backupPlanId;
        Type = type;
        StartedAtUtc = DateTime.UtcNow;
    }

    public void Start()
    {
        if (Status != "Pending")
            throw new InvalidOperationException(
                $"Backup job cannot be started because its current status is '{Status}'.");

        Status = "Running";
        ErrorMessage = null;
    }

    public void Complete(
        long bytesSelected,
        long bytesUploaded)
    {
        if (Status != "Running")
            throw new InvalidOperationException(
                $"Backup job cannot be completed because its current status is '{Status}'.");

        if (bytesSelected < 0)
            throw new ArgumentOutOfRangeException(
                nameof(bytesSelected));

        if (bytesUploaded < 0)
            throw new ArgumentOutOfRangeException(
                nameof(bytesUploaded));

        if (bytesUploaded > bytesSelected)
            throw new ArgumentOutOfRangeException(
                nameof(bytesUploaded),
                bytesUploaded,
                "Uploaded bytes cannot exceed selected bytes.");

        BytesSelected = bytesSelected;
        BytesUploaded = bytesUploaded;
        CompletedAtUtc = DateTime.UtcNow;
        Status = "Completed";
        ErrorMessage = null;
    }

    public void Fail(string errorMessage)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
            throw new ArgumentException(
                "An error message is required.",
                nameof(errorMessage));

        if (Status is "Completed" or "Cancelled" or "Failed")
            throw new InvalidOperationException(
                $"Backup job cannot be failed because its current status is '{Status}'.");

        CompletedAtUtc = DateTime.UtcNow;
        Status = "Failed";
        ErrorMessage = errorMessage.Trim();
    }

    public void Cancel()
    {
        if (Status is "Completed" or "Cancelled" or "Failed")
            throw new InvalidOperationException(
                $"Backup job cannot be cancelled because its current status is '{Status}'.");

        CompletedAtUtc = DateTime.UtcNow;
        Status = "Cancelled";
    }

    public void Abandon(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException(
                "An abandonment reason is required.",
                nameof(reason));

        if (Status is "Completed" or "Cancelled" or "Failed")
            return;

        CompletedAtUtc = DateTime.UtcNow;
        Status = "Failed";
        ErrorMessage = reason.Trim();
    }
}
