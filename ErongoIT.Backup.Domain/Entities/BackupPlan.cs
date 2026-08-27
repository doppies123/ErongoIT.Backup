using ErongoIT.Backup.Domain.Enums;

namespace ErongoIT.Backup.Domain.Entities;

public sealed class BackupPlan
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid CustomerId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public BackupScheduleType ScheduleType { get; private set; }

    public int IntervalMinutes { get; private set; }

    // Local time represented as minutes after midnight.
    // 0 = 00:00, 60 = 01:00, 1439 = 23:59.
    public int ScheduleTimeMinutes { get; private set; }

    // Sunday = 0 through Saturday = 6.
    // Used only for Weekly schedules.
    public int ScheduleDayOfWeek { get; private set; }

    public int RetentionDays { get; private set; }

    public bool IsEnabled { get; private set; } = true;

    private BackupPlan()
    {
    }

    public BackupPlan(
        Guid customerId,
        string name,
        BackupScheduleType scheduleType,
        int intervalMinutes,
        int scheduleTimeMinutes,
        int scheduleDayOfWeek,
        int retentionDays)
    {
        if (customerId == Guid.Empty)
            throw new ArgumentException(
                "Customer ID is required.",
                nameof(customerId));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Backup plan name is required.",
                nameof(name));

        ValidateScheduleType(scheduleType);
        ValidateInterval(scheduleType, intervalMinutes);
        ValidateScheduleTime(scheduleType, scheduleTimeMinutes);
        ValidateScheduleDay(scheduleType, scheduleDayOfWeek);
        ValidateRetention(retentionDays);

        CustomerId = customerId;
        Name = name.Trim();
        ScheduleType = scheduleType;
        IntervalMinutes = intervalMinutes;
        ScheduleTimeMinutes = scheduleTimeMinutes;
        ScheduleDayOfWeek = scheduleDayOfWeek;
        RetentionDays = retentionDays;
    }

    public void UpdateSchedule(
        BackupScheduleType scheduleType,
        int intervalMinutes,
        int scheduleTimeMinutes,
        int scheduleDayOfWeek,
        int retentionDays)
    {
        ValidateScheduleType(scheduleType);
        ValidateInterval(scheduleType, intervalMinutes);
        ValidateScheduleTime(scheduleType, scheduleTimeMinutes);
        ValidateScheduleDay(scheduleType, scheduleDayOfWeek);
        ValidateRetention(retentionDays);

        ScheduleType = scheduleType;
        IntervalMinutes = intervalMinutes;
        ScheduleTimeMinutes = scheduleTimeMinutes;
        ScheduleDayOfWeek = scheduleDayOfWeek;
        RetentionDays = retentionDays;
    }

    public void Enable()
    {
        IsEnabled = true;
    }

    public void Disable()
    {
        IsEnabled = false;
    }

    private static void ValidateScheduleType(
        BackupScheduleType scheduleType)
    {
        if (!Enum.IsDefined(scheduleType))
            throw new ArgumentOutOfRangeException(
                nameof(scheduleType),
                scheduleType,
                "A valid backup schedule type is required.");
    }

    private static void ValidateInterval(
        BackupScheduleType scheduleType,
        int intervalMinutes)
    {
        if (scheduleType != BackupScheduleType.Continuous)
            return;

        if (intervalMinutes < 15)
            throw new ArgumentOutOfRangeException(
                nameof(intervalMinutes),
                intervalMinutes,
                "Continuous backup interval must be at least 15 minutes.");
    }

    private static void ValidateScheduleTime(
        BackupScheduleType scheduleType,
        int scheduleTimeMinutes)
    {
        if (scheduleType == BackupScheduleType.Continuous)
            return;

        if (scheduleTimeMinutes < 0 ||
            scheduleTimeMinutes > 1439)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scheduleTimeMinutes),
                scheduleTimeMinutes,
                "Scheduled time must be between 00:00 and 23:59.");
        }
    }

    private static void ValidateScheduleDay(
        BackupScheduleType scheduleType,
        int scheduleDayOfWeek)
    {
        if (scheduleType != BackupScheduleType.Weekly)
            return;

        if (scheduleDayOfWeek < 0 ||
            scheduleDayOfWeek > 6)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scheduleDayOfWeek),
                scheduleDayOfWeek,
                "Weekly schedule day must be between Sunday and Saturday.");
        }
    }

    private static void ValidateRetention(int retentionDays)
    {
        if (retentionDays <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(retentionDays),
                retentionDays,
                "Retention must be greater than zero.");
    }
}
