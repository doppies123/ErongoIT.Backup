using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ErongoIT.Backup.Agent.Performance;

/// <summary>
/// Puts the whole Agent process into Windows "background processing mode".
///
/// In this mode Windows gives the process very low disk I/O priority,
/// low memory priority and idle CPU priority. When the laptop user opens
/// programs or files, their disk reads always go first and the backup
/// only uses the SSD/HDD when it would otherwise be idle.
///
/// This is the same mechanism Windows Search and Windows Defender
/// scheduled scans use.
/// </summary>
public static class BackgroundPriority
{
    private const uint PROCESS_MODE_BACKGROUND_BEGIN = 0x00100000;
    private const int ERROR_PROCESS_MODE_ALREADY_BACKGROUND = 402;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetPriorityClass(
        IntPtr hProcess,
        uint dwPriorityClass);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// Enters background mode. Returns a short description of what was applied.
    /// </summary>
    public static string Enter()
    {
        if (!OperatingSystem.IsWindows())
            return "not Windows - unchanged";

        if (SetPriorityClass(GetCurrentProcess(), PROCESS_MODE_BACKGROUND_BEGIN))
            return "background mode (very low disk I/O, idle CPU)";

        var error = Marshal.GetLastWin32Error();

        if (error == ERROR_PROCESS_MODE_ALREADY_BACKGROUND)
            return "background mode (already active)";

        // Fallback: at least lower the CPU priority.
        try
        {
            using var process = Process.GetCurrentProcess();
            process.PriorityClass = ProcessPriorityClass.Idle;

            return $"idle CPU priority only (background mode failed, error {error})";
        }
        catch (Exception ex)
        {
            return $"unchanged (error {error}: {ex.Message})";
        }
    }
}
