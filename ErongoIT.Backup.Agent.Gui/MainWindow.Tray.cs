using System.ComponentModel;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace ErongoIT.Backup.Agent.Gui;

/// <summary>
/// System-tray behaviour: the window's close button hides it to the tray;
/// the tray menu reopens it, starts a backup, or really exits.
/// Scheduled backups are done by the Windows service either way.
/// </summary>
public partial class MainWindow
{
    private WinForms.NotifyIcon? _trayIcon;
    private bool _exitRequested;
    private bool _trayHintShown;

    private void InitializeTray()
    {
        var menu = new WinForms.ContextMenuStrip();

        var openItem = menu.Items.Add("Open ErongoIT Backup", null, (_, _) => ShowFromTray());
        openItem.Font = new System.Drawing.Font(openItem.Font, System.Drawing.FontStyle.Bold);

        menu.Items.Add("Back up now", null, async (_, _) =>
        {
            ShowFromTray();

            if (!_backupRunning)
                await StartManualBackupAsync();
        });

        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitFromTray());

        _trayIcon = new WinForms.NotifyIcon
        {
            Text = "ErongoIT Backup",
            Icon = LoadTrayIcon(),
            ContextMenuStrip = menu,
            Visible = true
        };

        _trayIcon.DoubleClick += (_, _) => ShowFromTray();

        Closing += MainWindow_Closing;

        // Windows sign-out / shutdown must not be blocked by "hide to tray".
        System.Windows.Application.Current.SessionEnding += (_, _) => _exitRequested = true;
        Dispatcher.ShutdownStarted += (_, _) => _exitRequested = true;

        Closed += (_, _) =>
        {
            if (_trayIcon is null)
                return;

            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        };
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_exitRequested)
            return;

        // Close button (X): keep running in the tray.
        e.Cancel = true;
        Hide();

        if (!_trayHintShown && _trayIcon is not null)
        {
            _trayHintShown = true;

            _trayIcon.ShowBalloonTip(
                4000,
                "ErongoIT Backup is still running",
                "It is in the system tray. Backups continue automatically. " +
                "Right-click the icon and choose Exit to close it.",
                WinForms.ToolTipIcon.Info);
        }
    }

    /// <summary>Brings the window back (also used by the single-instance signal).</summary>
    public void ShowFromTray()
    {
        Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();

        // Force to the foreground.
        Topmost = true;
        Topmost = false;
        Focus();
    }

    /// <summary>Really closes the application.</summary>
    public void ExitApplication()
    {
        _exitRequested = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private void ExitFromTray()
    {
        if (_backupRunning)
        {
            ShowFromTray();

            var answer = MessageBox.Show(
                this,
                "A backup started from this window is still running. Exit and stop it?" +
                Environment.NewLine + Environment.NewLine +
                "(Scheduled backups by the background service are not affected.)",
                "ErongoIT Backup",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (answer != MessageBoxResult.Yes)
                return;

            _backupCancellation?.Cancel();
        }

        ExitApplication();
    }

    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/app.ico"));

            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new System.Drawing.Icon(stream, WinForms.SystemInformation.SmallIconSize);
            }
        }
        catch
        {
        }

        return System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
               ?? System.Drawing.SystemIcons.Application;
    }
}
