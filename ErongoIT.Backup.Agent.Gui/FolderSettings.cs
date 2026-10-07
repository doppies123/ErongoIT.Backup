using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using ErongoIT.Backup.Agent.Configuration;

namespace ErongoIT.Backup.Agent.Gui;

/// <summary>
/// Saves the list of folders to back up into
/// C:\ProgramData\ErongoIT Backup\agent.json.
///
/// That file belongs to the backup service and needs administrator
/// rights to change, so a normal user's GUI starts a short elevated copy
/// of itself ("--save-folders ...") that writes the file and exits.
/// The service picks up the new folders at the start of its next backup;
/// no restart is needed.
/// </summary>
public static class FolderSettings
{
    public const string SaveFoldersArgument = "--save-folders";

    public enum SaveResult
    {
        Saved,
        Cancelled,
        Failed
    }

    private static bool IsElevated =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent())
            .IsInRole(WindowsBuiltInRole.Administrator);

    /// <summary>Saves the folders, asking Windows for admin approval if needed.</summary>
    public static SaveResult Save(
        IReadOnlyList<string> folders,
        out string error)
    {
        error = string.Empty;

        if (IsElevated)
        {
            return WriteConfig(folders, out error)
                ? SaveResult.Saved
                : SaveResult.Failed;
        }

        // Base64 JSON avoids all quoting problems with paths like "D:\".
        var payload = Convert.ToBase64String(
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(folders)));

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ProcessPath!,
                Arguments = $"{SaveFoldersArgument} {payload}",
                UseShellExecute = true,
                Verb = "runas"
            });

            if (process is null)
            {
                error = "The settings helper could not be started.";
                return SaveResult.Failed;
            }

            if (!process.WaitForExit(TimeSpan.FromSeconds(60)))
            {
                error = "The settings helper did not finish in time.";
                return SaveResult.Failed;
            }

            if (process.ExitCode == 0)
                return SaveResult.Saved;

            error = process.ExitCode switch
            {
                2 => "This PC is not registered (agent.json is missing).",
                3 => "The folder list was not valid.",
                _ => $"Saving failed (code {process.ExitCode})."
            };

            return SaveResult.Failed;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The user clicked "No" on the Windows admin prompt.
            return SaveResult.Cancelled;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return SaveResult.Failed;
        }
    }

    /// <summary>
    /// Runs in the elevated helper process. Returns the process exit code:
    /// 0 = saved, 1 = error, 2 = not registered, 3 = invalid list.
    /// </summary>
    public static int RunElevatedSave(string[] args)
    {
        try
        {
            var index = Array.FindIndex(args, a =>
                string.Equals(a, SaveFoldersArgument, StringComparison.OrdinalIgnoreCase));

            if (index < 0 || index + 1 >= args.Length)
                return 3;

            var folders = JsonSerializer.Deserialize<List<string>>(
                Encoding.UTF8.GetString(
                    Convert.FromBase64String(args[index + 1])));

            if (folders is null || folders.Count == 0)
                return 3;

            if (!AgentConfigFile.Exists)
                return 2;

            return WriteConfig(folders, out _) ? 0 : 1;
        }
        catch (FormatException)
        {
            return 3;
        }
        catch (JsonException)
        {
            return 3;
        }
        catch
        {
            return 1;
        }
    }

    private static bool WriteConfig(
        IReadOnlyList<string> folders,
        out string error)
    {
        error = string.Empty;

        var cleaned = folders
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => Path.GetFullPath(f.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (cleaned.Count == 0)
        {
            error = "At least one folder must stay selected.";
            return false;
        }

        var config = AgentConfigFile.TryLoad();

        if (config is null)
        {
            error = "This PC is not registered (agent.json is missing).";
            return false;
        }

        try
        {
            config.SourcePaths = cleaned;
            config.Save();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
