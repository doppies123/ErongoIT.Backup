using ErongoIT.Backup.Agent.Configuration;

namespace ErongoIT.Backup.Agent.Setup;

/// <summary>
/// Command-line enrollment, used by the installer and for scripted installs:
///
///   ErongoIT.Backup.Agent.exe --enroll
///       --server https://backup.erongoit.com
///       --username admin --password "..."
///       --customer "Erongo IT Consultants"   (name or ID)
///       [--name PC-NAME]                      (default: computer name)
///       [--plan "Daily Test"]                 (name or ID)
///       --folder "C:\Users\Bob\Documents" [--folder "D:\Data" ...]
///
/// Writes C:\ProgramData\ErongoIT Backup\agent.json. The admin password is
/// used only to register and is not stored.
/// </summary>
public static class EnrollmentCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var server = Get(args, "--server") ?? "https://backup.erongoit.com";
            var username = Get(args, "--username") ?? throw new ArgumentException("--username is required.");
            var password = Get(args, "--password") ?? throw new ArgumentException("--password is required.");
            var customerArg = Get(args, "--customer") ?? throw new ArgumentException("--customer is required.");
            var deviceName = Get(args, "--name") ?? Environment.MachineName;
            var planArg = Get(args, "--plan");
            var folders = GetAll(args, "--folder");

            if (folders.Count == 0)
                throw new ArgumentException("At least one --folder is required.");

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                    throw new ArgumentException($"Folder does not exist: {folder}");
            }

            using var client = new EnrollmentClient(server);

            Console.WriteLine($"Signing in to {client.ServerUrl} ...");
            await client.LoginAsync(username, password);

            var customers = await client.GetCustomersAsync();
            var customer = Find(customers, customerArg)
                           ?? throw new ArgumentException($"Customer not found: {customerArg}");

            Guid? planId = null;

            if (!string.IsNullOrWhiteSpace(planArg))
            {
                var plans = await client.GetBackupPlansAsync(customer.Id);
                planId = (Find(plans, planArg)
                          ?? throw new ArgumentException($"Backup plan not found for {customer.Name}: {planArg}")).Id;
            }

            Console.WriteLine($"Enrolling '{deviceName}' for {customer.Name} ...");
            var result = await client.EnrollAsync(customer.Id, deviceName, planId);

            var config = new AgentConfigFile
            {
                ApiBaseUrl = client.ServerUrl,
                CustomerId = result.CustomerId,
                DeviceId = result.DeviceId,
                DeviceName = result.Name,
                SourcePaths = folders.Select(Path.GetFullPath).ToList(),
                EnrolledAtUtc = DateTime.UtcNow
            };

            config.SetDeviceKey(result.DeviceKey);
            config.Save();

            Console.WriteLine(result.IsNewDevice
                ? $"Registered new device {result.DeviceId}."
                : $"Re-enrolled existing device {result.DeviceId} (backup history kept).");
            Console.WriteLine($"Configuration saved to {AgentConfigFile.ConfigPath}");

            if (result.AssignedBackupPlanId is null)
                Console.WriteLine("WARNING: no backup plan is assigned to this device yet. Assign one in the portal.");

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"ENROLLMENT FAILED: {ex.Message}");
            return 1;
        }
    }

    private static NamedItem? Find(IReadOnlyList<NamedItem> items, string nameOrId)
    {
        if (Guid.TryParse(nameOrId, out var id))
            return items.FirstOrDefault(x => x.Id == id);

        return items.FirstOrDefault(x =>
            string.Equals(x.Name, nameOrId.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private static string? Get(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    private static List<string> GetAll(string[] args, string name)
    {
        var values = new List<string>();

        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                values.Add(args[i + 1]);
        }

        return values;
    }
}
