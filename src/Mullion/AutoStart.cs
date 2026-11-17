using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;
using Mullion.Core;

namespace Mullion;

/// <summary>
/// Starts Mullion when the user signs in. Ordinarily that is an entry in the
/// per-user "Run" registry key. When Mullion is to run as administrator it is a
/// scheduled task instead, because the Run key would make Windows ask for
/// permission at every sign-in. These are the only things a portable copy stores
/// outside its own folder.
/// </summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Mullion";

    // Asking the Task Scheduler takes a moment, so the answer is kept until
    // Mullion itself changes it.
    private static bool? _taskPointsHere;

    private static string Command => $"\"{Environment.ProcessPath}\"";

    private static string TaskName => ElevatedStartTask.NameFor(Environment.UserName);

    /// <summary>True when Mullion starts at sign-in, either way, and it is this copy that starts.</summary>
    public static bool IsEnabled => RunEntryPointsHere || TaskPointsHere;

    /// <summary>True when the scheduled task is what starts this copy.</summary>
    public static bool UsesTask => TaskPointsHere;

    /// <summary>Switches starting at sign-in on or off.</summary>
    /// <param name="enabled">Whether Mullion starts when the user signs in.</param>
    /// <param name="asAdministrator">
    /// Whether it should start as administrator. That needs the scheduled task,
    /// which only a copy running as administrator can create; otherwise the Run
    /// key is used and Mullion asks for permission when it starts.
    /// </param>
    public static void SetEnabled(bool enabled, bool asAdministrator)
    {
        bool useTask = enabled && NeedsTask(asAdministrator) && RegisterTask();

        if (!useTask)
        {
            DeleteTask();
        }

        SetRunEntry(enabled && !useTask);
    }

    /// <summary>
    /// Keeps starting at sign-in, if it is switched on, in the form that suits how
    /// Mullion is to run: the task for administrator, the Run key otherwise.
    /// </summary>
    public static void Sync(bool asAdministrator)
    {
        if (!IsEnabled)
        {
            return;
        }

        bool wantTask = NeedsTask(asAdministrator);
        if (wantTask != TaskPointsHere || wantTask == RunEntryPointsHere)
        {
            SetEnabled(true, asAdministrator);
        }
    }

    /// <summary>
    /// The task is for starting as administrator without the permission question.
    /// Only a copy that has the rights can create it, and where Windows never asks
    /// the question the Run key does just as well.
    /// </summary>
    private static bool NeedsTask(bool asAdministrator) =>
        asAdministrator && Elevation.IsElevated && !Elevation.IsAlwaysElevated;

    private static bool RunEntryPointsHere
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string value
                    && string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    private static bool TaskPointsHere => _taskPointsHere ??= ReadTask();

    private static void SetRunEntry(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (enabled)
            {
                key.SetValue(ValueName, Command);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            Log.Write("Could not change the start-with-Windows setting.", ex);
        }
    }

    private static bool ReadTask()
    {
        try
        {
            dynamic folder = RootFolder();
            dynamic task = folder.GetTask(TaskName);
            string? command = ElevatedStartTask.CommandOf((string)task.Xml);
            return string.Equals(command, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (IsSchedulerFailure(ex))
        {
            // No such task, or it may not be read: either way it does not start this copy
            // as far as Mullion can tell.
            Log.Trace($"start task not read: {ex.GetType().Name} 0x{ex.HResult:X8}");
            return false;
        }
    }

    private static bool RegisterTask()
    {
        if (Environment.ProcessPath is not { } path)
        {
            return false;
        }

        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            string xml = ElevatedStartTask.BuildXml(path, identity.User?.Value ?? identity.Name);

            // 6: create the task, or replace it. 3: it runs as the signed-in user,
            // on their desktop, with no stored password.
            dynamic folder = RootFolder();
            folder.RegisterTask(TaskName, xml, 6, null, null, 3, null);
            _taskPointsHere = true;
            return true;
        }
        catch (Exception ex) when (IsSchedulerFailure(ex))
        {
            Log.Write("Could not create the task that starts Mullion as administrator.", ex);
            _taskPointsHere = null;
            return false;
        }
    }

    private static void DeleteTask()
    {
        try
        {
            dynamic folder = RootFolder();
            folder.DeleteTask(TaskName, 0);
        }
        catch (System.IO.FileNotFoundException)
        {
            // There was no task.
        }
        catch (Exception ex) when (IsSchedulerFailure(ex))
        {
            // A task created as administrator can only be removed as administrator.
            Log.Write("Could not remove the task that starts Mullion as administrator.", ex);
        }

        _taskPointsHere = null;
    }

    private static dynamic RootFolder()
    {
        var type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("The Task Scheduler is not available.");
        dynamic service = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("The Task Scheduler is not available.");
        service.Connect();
        return service.GetFolder("\\");
    }

    private static bool IsSchedulerFailure(Exception ex) =>
        ex is COMException
            or UnauthorizedAccessException
            or System.IO.IOException
            or InvalidOperationException
            or InvalidCastException
            or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException;
}
