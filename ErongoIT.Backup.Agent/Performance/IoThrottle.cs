using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ErongoIT.Backup.Agent.Performance;

/// <summary>
/// Limits how fast the Agent reads files from disk (hashing, compressing
/// and uploading all go through this).
///
/// Two speeds:
///   - Normal: when the PC is quiet.
///   - Busy:   when total CPU use is above BusyCpuPercent (the user is
///             working), the read speed drops so the backup barely
///             touches the disk.
///
/// The background Agent sets <see cref="Current"/> at startup. The GUI
/// never sets it, so manual GUI backups are not throttled.
/// </summary>
public sealed class IoThrottle
{
    public static IoThrottle? Current { get; set; }

    private readonly double _normalBytesPerSecond;
    private readonly double _busyBytesPerSecond;
    private readonly int _busyCpuPercent;

    private readonly object _lock = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    private double _tokens;
    private double _lastRefillSeconds;

    private double _lastCpuSampleSeconds = double.MinValue;
    private ulong _lastIdle;
    private ulong _lastKernel;
    private ulong _lastUser;
    private bool _isBusy;

    /// <param name="normalMegabytesPerSecond">0 = unlimited when the PC is quiet.</param>
    /// <param name="busyMegabytesPerSecond">Read speed while the user is busy. 0 = same as normal.</param>
    /// <param name="busyCpuPercent">Total CPU % above which the PC counts as busy. 0 = never.</param>
    public IoThrottle(
        int normalMegabytesPerSecond,
        int busyMegabytesPerSecond,
        int busyCpuPercent)
    {
        _normalBytesPerSecond = Math.Max(0, normalMegabytesPerSecond) * 1024d * 1024d;
        _busyBytesPerSecond = Math.Max(0, busyMegabytesPerSecond) * 1024d * 1024d;
        _busyCpuPercent = Math.Clamp(busyCpuPercent, 0, 100);
    }

    /// <summary>True while the last CPU sample was above the busy threshold.</summary>
    public bool IsBusy => _isBusy;

    public string Describe()
    {
        static string Rate(double bytes) =>
            bytes <= 0 ? "unlimited" : $"{bytes / 1024 / 1024:0} MB/s";

        return _busyCpuPercent > 0 && _busyBytesPerSecond > 0
            ? $"{Rate(_normalBytesPerSecond)} when quiet, {Rate(_busyBytesPerSecond)} when CPU > {_busyCpuPercent}%"
            : Rate(_normalBytesPerSecond);
    }

    /// <summary>Call after reading <paramref name="bytes"/>; waits if over the limit.</summary>
    public async ValueTask WaitAsync(
        int bytes,
        CancellationToken cancellationToken)
    {
        var delay = Consume(bytes);

        if (delay > TimeSpan.Zero)
            await Task.Delay(delay, cancellationToken);
    }

    /// <summary>Synchronous version for Stream.Read.</summary>
    public void Wait(int bytes)
    {
        var delay = Consume(bytes);

        if (delay > TimeSpan.Zero)
            Thread.Sleep(delay);
    }

    private TimeSpan Consume(int bytes)
    {
        if (bytes <= 0)
            return TimeSpan.Zero;

        lock (_lock)
        {
            var now = _clock.Elapsed.TotalSeconds;

            SampleCpu(now);

            var rate = CurrentRate();

            if (rate <= 0)
            {
                _tokens = 0;
                _lastRefillSeconds = now;
                return TimeSpan.Zero;
            }

            // Token bucket: allow at most a quarter-second burst.
            _tokens = Math.Min(
                rate * 0.25,
                _tokens + (now - _lastRefillSeconds) * rate);

            _lastRefillSeconds = now;
            _tokens -= bytes;

            if (_tokens >= 0)
                return TimeSpan.Zero;

            var seconds = Math.Min(5, -_tokens / rate);

            return TimeSpan.FromSeconds(seconds);
        }
    }

    private double CurrentRate()
    {
        if (_isBusy && _busyBytesPerSecond > 0)
        {
            return _normalBytesPerSecond <= 0
                ? _busyBytesPerSecond
                : Math.Min(_busyBytesPerSecond, _normalBytesPerSecond);
        }

        return _normalBytesPerSecond;
    }

    // ------------------------------------------------------------
    // Whole-system CPU use, sampled at most every 2 seconds.
    // ------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;

        public readonly ulong Value => ((ulong)High << 32) | Low;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(
        out FILETIME idleTime,
        out FILETIME kernelTime,
        out FILETIME userTime);

    private void SampleCpu(double now)
    {
        if (_busyCpuPercent <= 0 || !OperatingSystem.IsWindows())
            return;

        if (now - _lastCpuSampleSeconds < 2)
            return;

        if (!GetSystemTimes(out var idle, out var kernel, out var user))
            return;

        var first = _lastCpuSampleSeconds == double.MinValue;

        var idleDelta = idle.Value - _lastIdle;
        var totalDelta = (kernel.Value - _lastKernel) + (user.Value - _lastUser); // kernel includes idle

        _lastIdle = idle.Value;
        _lastKernel = kernel.Value;
        _lastUser = user.Value;
        _lastCpuSampleSeconds = now;

        if (first || totalDelta == 0)
            return;

        var busyPercent = 100d * (1d - (double)idleDelta / totalDelta);

        _isBusy = busyPercent >= _busyCpuPercent;
    }
}
