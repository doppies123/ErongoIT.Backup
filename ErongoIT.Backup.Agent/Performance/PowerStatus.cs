using System.Runtime.InteropServices;

namespace ErongoIT.Backup.Agent.Performance;

/// <summary>Detects whether a laptop is running on battery.</summary>
public static class PowerStatus
{
    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;        // 0 = battery, 1 = mains, 255 = unknown
        public byte BatteryFlag;
        public byte BatteryLifePercent;  // 0-100, 255 = unknown
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);

    /// <summary>True only when Windows reports the PC is unplugged.</summary>
    public static bool IsOnBattery(out int batteryPercent)
    {
        batteryPercent = -1;

        if (!OperatingSystem.IsWindows())
            return false;

        if (!GetSystemPowerStatus(out var status))
            return false;

        if (status.BatteryLifePercent <= 100)
            batteryPercent = status.BatteryLifePercent;

        return status.ACLineStatus == 0;
    }
}
