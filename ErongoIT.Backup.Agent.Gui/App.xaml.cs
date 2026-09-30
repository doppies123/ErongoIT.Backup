using System.IO;
using System.Threading;
using System.Windows;
using ErongoIT.Backup.Agent.Configuration;

namespace ErongoIT.Backup.Agent.Gui;

public partial class App : System.Windows.Application
{
    private const string InstanceMutexName = @"Local\ErongoIT.Backup.Gui";
    private const string ShowEventName = @"Local\ErongoIT.Backup.Gui.Show";

    private static Mutex? _instanceMutex;
    private static EventWaitHandle? _showEvent;

    protected override void OnStartup(
        StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args;

        var setupRequested = args.Any(a =>
            string.Equals(a, "--setup", StringComparison.OrdinalIgnoreCase));

        // Developer profiles (agentgui.*.json next to the exe) skip setup.
        var developerProfile =
            args.Any(a => string.Equals(a, "--profile", StringComparison.OrdinalIgnoreCase)) ||
            File.Exists(Path.Combine(AppContext.BaseDirectory, "agentgui.vps.json")) ||
            File.Exists(Path.Combine(AppContext.BaseDirectory, "agentgui.local.json"));

        if (setupRequested || (!AgentConfigFile.Exists && !developerProfile))
        {
            new SetupWindow().Show();
            return;
        }

        // One GUI per user session: a second start (e.g. the desktop
        // shortcut while the app sits in the tray) just shows the first one.
        _instanceMutex = new Mutex(true, InstanceMutexName, out var isFirstInstance);

        if (!isFirstInstance)
        {
            try
            {
                EventWaitHandle.OpenExisting(ShowEventName).Set();
            }
            catch
            {
            }

            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);

        ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            (_, _) => Dispatcher.InvokeAsync(() =>
                Windows.OfType<MainWindow>().FirstOrDefault()?.ShowFromTray()),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);

        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _showEvent?.Dispose();

        if (_instanceMutex is not null)
        {
            try
            {
                _instanceMutex.ReleaseMutex();
            }
            catch
            {
            }

            _instanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
