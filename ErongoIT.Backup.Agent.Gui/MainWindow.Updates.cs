using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace ErongoIT.Backup.Agent.Gui;

/// <summary>
/// Automatic updates. The tray app checks the server shortly after start
/// and then every 6 hours. When a newer version is published it asks
/// "Update to version x.y.z?"; on Yes it downloads the installer, checks
/// its SHA-256 and starts the setup wizard (which closes this app,
/// upgrades the service and keeps the PC's registration).
/// Settings > Updates has a manual "Check for updates" button.
/// </summary>
public partial class MainWindow
{
    private static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
    private static readonly TimeSpan RemindLaterAfter = TimeSpan.FromHours(24);

    private DispatcherTimer? _updateTimer;
    private UpdateInfoDto? _availableUpdate;
    private bool _updateBusy;

    private void InitializeUpdates()
    {
        UpdateCurrentVersionText.Text = $"Installed version: {AppVersion}";
        SetUpdateStatus("Updates are checked automatically every 6 hours.", showInstall: false);

        _updateTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = FirstUpdateCheckDelay
        };

        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = UpdateCheckInterval;
            await CheckForUpdatesAsync(userInitiated: false);
        };

        _updateTimer.Start();
    }

    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(userInitiated: true);
    }

    private async void InstallUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_availableUpdate is not null)
            await DownloadAndInstallAsync(_availableUpdate);
    }

    private async Task CheckForUpdatesAsync(bool userInitiated)
    {
        if (_updateBusy)
            return;

        _updateBusy = true;

        try
        {
            if (userInitiated)
                SetUpdateStatus("Checking for updates...", showInstall: false);

            UpdateInfoDto? latest;

            try
            {
                latest = await _api.GetLatestUpdateAsync();
            }
            catch
            {
                // Token may have expired while sitting in the tray: sign in again once.
                if (_options.IsEnrolled)
                    await _api.DeviceLoginAsync(_options.DeviceId, _options.DeviceKey);
                else
                    await _api.LoginAsync(_options.Username, _options.Password);

                latest = await _api.GetLatestUpdateAsync();
            }

            if (latest is null || !IsNewer(latest.Version, AppVersion))
            {
                _availableUpdate = null;
                SetUpdateStatus($"You have the latest version ({AppVersion}). Last checked {DateTime.Now:dd MMM HH:mm}.", showInstall: false);

                if (userInitiated)
                {
                    MessageBox.Show(
                        this,
                        $"You have the latest version of ErongoIT Backup ({AppVersion}).",
                        "ErongoIT Backup update",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }

                return;
            }

            _availableUpdate = latest;
            SetUpdateStatus($"Version {latest.Version} is available.", showInstall: true);

            if (userInitiated || ShouldPrompt(latest.Version))
                await PromptForUpdateAsync(latest);
        }
        catch (Exception ex)
        {
            if (userInitiated)
            {
                SetUpdateStatus($"Could not check for updates: {ex.Message}", showInstall: _availableUpdate is not null);

                MessageBox.Show(
                    this,
                    $"Could not check for updates. Check the internet connection and try again.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                    "ErongoIT Backup update",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            _updateBusy = false;
        }
    }

    private bool ShouldPrompt(string version)
    {
        return _guiSettings.UpdatePromptedVersion != version ||
               _guiSettings.UpdatePromptedUtc is null ||
               DateTime.UtcNow - _guiSettings.UpdatePromptedUtc.Value > RemindLaterAfter;
    }

    private async Task PromptForUpdateAsync(UpdateInfoDto update)
    {
        _guiSettings.UpdatePromptedVersion = update.Version;
        _guiSettings.UpdatePromptedUtc = DateTime.UtcNow;
        _guiSettings.Save();

        _trayIcon?.ShowBalloonTip(
            6000,
            "ErongoIT Backup update available",
            $"Version {update.Version} is ready to install.",
            WinForms.ToolTipIcon.Info);

        var notes = string.IsNullOrWhiteSpace(update.Notes)
            ? string.Empty
            : Environment.NewLine + Environment.NewLine + "What's new:" + Environment.NewLine + update.Notes.Trim();

        // DefaultDesktopOnly: shows on top even while the app sits in the tray.
        var answer = MessageBox.Show(
            $"A new version of ErongoIT Backup is available.{Environment.NewLine}{Environment.NewLine}" +
            $"Installed: {AppVersion}{Environment.NewLine}" +
            $"New: {update.Version}" +
            notes +
            $"{Environment.NewLine}{Environment.NewLine}Would you like to update to version {update.Version} now?",
            "ErongoIT Backup update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes,
            MessageBoxOptions.DefaultDesktopOnly);

        if (answer == MessageBoxResult.Yes)
            await DownloadAndInstallAsync(update);
        else
            SetUpdateStatus($"Version {update.Version} is available. You will be reminded tomorrow.", showInstall: true);
    }

    private async Task DownloadAndInstallAsync(UpdateInfoDto update)
    {
        if (_updateBusy && UpdateProgress.Visibility == Visibility.Visible)
            return;

        _updateBusy = true;
        InstallUpdateButton.IsEnabled = false;
        UpdateProgress.Value = 0;
        UpdateProgress.Visibility = Visibility.Visible;

        try
        {
            var folder = Path.Combine(Path.GetTempPath(), "ErongoIT.Backup.Update");
            Directory.CreateDirectory(folder);

            var installer = Path.Combine(folder, update.FileName);

            SetUpdateStatus($"Downloading version {update.Version}...", showInstall: true);

            var progress = new Progress<double>(p =>
            {
                UpdateProgress.Value = p * 100;
                SetUpdateStatus($"Downloading version {update.Version}... {p:P0}", showInstall: true);
            });

            await _api.DownloadUpdateAsync(update, installer, progress);

            SetUpdateStatus("Checking the download...", showInstall: true);

            if (!string.IsNullOrWhiteSpace(update.Sha256))
            {
                var actual = await Task.Run(() => Sha256Of(installer));

                if (!string.Equals(actual, update.Sha256.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(installer);
                    throw new InvalidOperationException("The downloaded file is damaged (checksum mismatch). Please try again.");
                }
            }

            SetUpdateStatus($"Starting the installer for version {update.Version}...", showInstall: true);

            try
            {
                // Let the installer take the foreground (otherwise Windows may
                // only flash it on the taskbar).
                AllowSetForegroundWindow(AsfwAny);

                // The installer asks for administrator approval, closes this
                // app, upgrades the service and keeps this PC's registration.
                Process.Start(new ProcessStartInfo
                {
                    FileName = installer,
                    UseShellExecute = true
                });

                // Give the installer a moment to appear before this app exits.
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
            catch (System.ComponentModel.Win32Exception)
            {
                SetUpdateStatus("The update was cancelled (administrator approval is needed).", showInstall: true);
                return;
            }

            ExitApplication();
        }
        catch (Exception ex)
        {
            SetUpdateStatus($"Update failed: {ex.Message}", showInstall: true);

            MessageBox.Show(
                $"The update could not be installed.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "ErongoIT Backup update",
                MessageBoxButton.OK,
                MessageBoxImage.Warning,
                MessageBoxResult.OK,
                MessageBoxOptions.DefaultDesktopOnly);
        }
        finally
        {
            _updateBusy = false;
            InstallUpdateButton.IsEnabled = true;
            UpdateProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void SetUpdateStatus(string text, bool showInstall)
    {
        UpdateStatusText.Text = text;
        InstallUpdateButton.Visibility = showInstall ? Visibility.Visible : Visibility.Collapsed;

        if (_availableUpdate is not null)
            InstallUpdateButton.Content = $"Update to {_availableUpdate.Version}";
    }

    private const int AsfwAny = -1;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);

    private static bool IsNewer(string candidate, string current)
    {
        return Version.TryParse(candidate, out var a) &&
               Version.TryParse(current, out var b) &&
               a > b;
    }

    private static string Sha256Of(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
