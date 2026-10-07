using System.Windows;
using System.Windows.Threading;

namespace ErongoIT.Backup.Agent.Gui;

/// <summary>
/// Keeps the window up to date while it is open: every 30 seconds the
/// backup jobs are reloaded from the server, so History, the Overview
/// status and the "current backup" panel show what the background
/// service is doing without having to restart the app.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan AutoRefreshInterval = TimeSpan.FromSeconds(30);

    private DispatcherTimer? _autoRefreshTimer;
    private bool _autoRefreshBusy;

    private void InitializeAutoRefresh()
    {
        _autoRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = AutoRefreshInterval
        };

        _autoRefreshTimer.Tick += async (_, _) => await AutoRefreshAsync();
        _autoRefreshTimer.Start();
    }

    private async Task AutoRefreshAsync()
    {
        // Skip while hidden in the tray, while a manual backup from this
        // window is running (it refreshes itself), or if a refresh is busy.
        if (_autoRefreshBusy || _backupRunning || !IsVisible || WindowState == WindowState.Minimized)
            return;

        _autoRefreshBusy = true;

        try
        {
            IReadOnlyList<BackupJobDto> jobs;

            try
            {
                jobs = await _api.GetBackupJobsAsync(_options.DeviceId);
            }
            catch
            {
                // The sign-in token expires after a few hours: sign in again once.
                if (_options.IsEnrolled)
                    await _api.DeviceLoginAsync(_options.DeviceId, _options.DeviceKey);
                else
                    await _api.LoginAsync(_options.Username, _options.Password);

                jobs = await _api.GetBackupJobsAsync(_options.DeviceId);
            }

            _jobs = jobs;

            UpdateLatestBackup();
            UpdateCurrentJob();

            if (HistoryView.Visibility == Visibility.Visible)
            {
                SetHistoryRows(
                    _jobs
                        .OrderByDescending(x => x.StartedAtUtc)
                        .Select(x => new HistoryRow(x))
                        .ToList(),
                    keepPage: true);
            }
        }
        catch
        {
            // Offline or server unavailable: try again on the next tick.
        }
        finally
        {
            _autoRefreshBusy = false;
        }
    }
}
