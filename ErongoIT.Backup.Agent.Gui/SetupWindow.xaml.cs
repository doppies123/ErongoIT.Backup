using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Input;
using ErongoIT.Backup.Agent.Configuration;
using ErongoIT.Backup.Agent.Setup;

namespace ErongoIT.Backup.Agent.Gui;

/// <summary>
/// First-run / re-registration window. Signs in with an admin account
/// (only for this step), enrolls the PC, writes
/// C:\ProgramData\ErongoIT Backup\agent.json and restarts the Agent service.
/// Needs administrator rights (it relaunches itself elevated if required).
/// </summary>
public partial class SetupWindow : Window
{
    public const string AgentServiceName = "ErongoITBackupAgent";

    private static readonly NamedItem NoPlan =
        new(Guid.Empty, "— none (assign later in the portal) —");

    private readonly ObservableCollection<string> _folders = new();
    private EnrollmentClient? _client;

    public SetupWindow()
    {
        InitializeComponent();

        FolderList.ItemsSource = _folders;

        var existing = AgentConfigFile.TryLoad();

        if (existing is not null)
        {
            ServerBox.Text = existing.ApiBaseUrl;
            DeviceNameBox.Text = existing.DeviceName;

            foreach (var folder in existing.SourcePaths)
                _folders.Add(folder);

            StatusText.Text =
                $"This PC is already registered as '{existing.DeviceName}'. " +
                "Registering again keeps its backup history.";
        }
        else
        {
            DeviceNameBox.Text = Environment.MachineName;

            foreach (var folder in new[]
                     {
                         Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                         Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
                     })
            {
                if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder))
                    _folders.Add(folder);
            }
        }

        Loaded += (_, _) => EnsureElevated();
    }

    private static bool LaunchedByInstaller =>
        Environment.GetCommandLineArgs().Any(a =>
            string.Equals(a, "--installer", StringComparison.OrdinalIgnoreCase));

    private static bool IsElevated =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Writing ProgramData and restarting the service need admin rights.</summary>
    private void EnsureElevated()
    {
        if (IsElevated)
            return;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = "--setup",
                UseShellExecute = true,
                Verb = "runas"
            });

            System.Windows.Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(
                this,
                "Administrator rights are required to register this PC.",
                "ErongoIT Backup Setup",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            Close();
        }
    }

    private void PasswordBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            SignIn_Click(sender, e);
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Signing in...");

        try
        {
            _client?.Dispose();
            _client = new EnrollmentClient(ServerBox.Text.Trim());

            await _client.LoginAsync(
                UsernameBox.Text.Trim(),
                PasswordBox.Password);

            var customers = await _client.GetCustomersAsync();

            if (customers.Count == 0)
                throw new InvalidOperationException(
                    "No customers exist on the server yet. Create one in the portal first.");

            CustomerCombo.ItemsSource = customers;

            var existing = AgentConfigFile.TryLoad();

            CustomerCombo.SelectedItem =
                customers.FirstOrDefault(c => existing is not null && c.Id == existing.CustomerId)
                ?? (customers.Count == 1 ? customers[0] : null);

            Enable(CustomerCard, true);
            Enable(FoldersCard, true);

            SetBusy(false, "Signed in. Choose the customer, plan and folders.");
        }
        catch (Exception ex)
        {
            SetBusy(false, $"Sign in failed: {ex.Message}");
        }
    }

    private async void CustomerCombo_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        PlanCombo.ItemsSource = null;
        UpdateRegisterButton();

        if (_client is null || CustomerCombo.SelectedItem is not NamedItem customer)
            return;

        try
        {
            var plans = new List<NamedItem> { NoPlan };
            plans.AddRange(await _client.GetBackupPlansAsync(customer.Id));

            PlanCombo.ItemsSource = plans;
            PlanCombo.SelectedIndex = plans.Count > 1 ? 1 : 0;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load backup plans: {ex.Message}";
        }

        UpdateRegisterButton();
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select a folder to back up",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        foreach (var folder in dialog.FolderNames)
        {
            if (!_folders.Contains(folder, StringComparer.OrdinalIgnoreCase))
                _folders.Add(folder);
        }

        UpdateRegisterButton();
    }

    private void RemoveFolder_Click(object sender, RoutedEventArgs e)
    {
        if (FolderList.SelectedItem is string folder)
            _folders.Remove(folder);

        UpdateRegisterButton();
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        if (_client is null || CustomerCombo.SelectedItem is not NamedItem customer)
            return;

        var deviceName = DeviceNameBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(deviceName))
        {
            StatusText.Text = "Enter a computer name.";
            return;
        }

        if (_folders.Count == 0)
        {
            StatusText.Text = "Add at least one folder to back up.";
            return;
        }

        var plan = PlanCombo.SelectedItem as NamedItem;
        Guid? planId = plan is null || plan.Id == Guid.Empty ? null : plan.Id;

        SetBusy(true, "Registering this PC...");

        try
        {
            var result = await _client.EnrollAsync(
                customer.Id,
                deviceName,
                planId);

            var config = new AgentConfigFile
            {
                ApiBaseUrl = _client.ServerUrl,
                CustomerId = result.CustomerId,
                DeviceId = result.DeviceId,
                DeviceName = result.Name,
                SourcePaths = _folders.ToList(),
                EnrolledAtUtc = DateTime.UtcNow
            };

            config.SetDeviceKey(result.DeviceKey);
            config.Save();

            StatusText.Text = "Restarting the backup service...";
            var serviceMessage = await Task.Run(RestartAgentService);

            MessageBox.Show(
                this,
                (result.IsNewDevice
                    ? $"'{result.Name}' is registered for {customer.Name}."
                    : $"'{result.Name}' was registered again (backup history kept).") +
                Environment.NewLine + Environment.NewLine +
                serviceMessage +
                (planId is null
                    ? Environment.NewLine + Environment.NewLine +
                      "No backup plan was chosen: assign one in the portal before backups can run."
                    : string.Empty),
                "ErongoIT Backup",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Launched by the installer: just finish, the installer
            // starts the service and offers to open the GUI itself
            // (as the normal user, not as administrator).
            if (!LaunchedByInstaller)
                new MainWindow().Show();

            Close();
        }
        catch (Exception ex)
        {
            SetBusy(false, $"Registration failed: {ex.Message}");
        }
    }

    private static string RestartAgentService()
    {
        try
        {
            using var service = ServiceController.GetServices()
                .FirstOrDefault(s => s.ServiceName == AgentServiceName);

            if (service is null)
                return "The backup service is not installed on this PC yet.";

            if (service.Status != ServiceControllerStatus.Stopped)
            {
                service.Stop();
                service.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(30));

            return "The backup service is running with the new settings.";
        }
        catch (Exception ex)
        {
            return $"Settings saved, but the backup service could not be restarted: {ex.Message}";
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void SetBusy(bool busy, string message)
    {
        StatusText.Text = message;
        SignInButton.IsEnabled = !busy;
        Cursor = busy ? Cursors.Wait : null;

        if (busy)
            RegisterButton.IsEnabled = false;
        else
            UpdateRegisterButton();
    }

    private void UpdateRegisterButton()
    {
        RegisterButton.IsEnabled =
            _client is not null &&
            CustomerCombo.SelectedItem is NamedItem &&
            _folders.Count > 0;
    }

    private static void Enable(System.Windows.Controls.Border card, bool enabled)
    {
        card.IsEnabled = enabled;
        card.Opacity = enabled ? 1.0 : 0.5;
    }

    protected override void OnClosed(EventArgs e)
    {
        _client?.Dispose();
        base.OnClosed(e);
    }
}
