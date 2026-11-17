using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;

namespace Mullion;

/// <summary>Whether Mullion runs as administrator, and starting another copy that does or does not.</summary>
internal static class Elevation
{
    /// <summary>True when this copy runs as administrator.</summary>
    public static bool IsElevated { get; } = Check();

    /// <summary>
    /// True when everything this user starts runs as administrator, because
    /// Windows' permission question (User Account Control) is switched off or the
    /// account is the built-in administrator. Asking for the rights, or giving
    /// them up, then changes nothing.
    /// </summary>
    public static bool IsAlwaysElevated { get; } = IsElevated && !HasSplitToken();

    /// <summary>
    /// Starts this program again as administrator. Windows asks the user for
    /// permission first; false when they say no or it cannot be started.
    /// </summary>
    public static bool StartElevated(IEnumerable<string> arguments)
    {
        if (Environment.ProcessPath is not { } path)
        {
            return false;
        }

        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = AppContext.BaseDirectory,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            return true;
        }
        catch (Win32Exception ex)
        {
            // 1223: the user answered "No" to the permission question.
            if (ex.NativeErrorCode != 1223)
            {
                Log.Write("Could not start Mullion as administrator.", ex);
            }

            return false;
        }
    }

    /// <summary>Starts this program again with the rights this copy has.</summary>
    public static bool StartSame(IEnumerable<string> arguments)
    {
        if (Environment.ProcessPath is not { } path)
        {
            return false;
        }

        var start = new ProcessStartInfo(path) { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Write("Could not restart Mullion.", ex);
            return false;
        }
    }

    /// <summary>
    /// Starts this program again without administrator rights. A program running
    /// as administrator passes its rights to whatever it starts, so the start is
    /// handed to the Windows shell, which runs as the ordinary user.
    /// </summary>
    public static bool StartNormal()
    {
        if (Environment.ProcessPath is not { } path)
        {
            return false;
        }

        try
        {
            string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var process = Process.Start(new ProcessStartInfo(explorer, $"\"{path}\"") { UseShellExecute = false });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            Log.Write("Could not restart Mullion without administrator rights.", ex);
            return false;
        }
    }

    /// <summary>
    /// True when this copy's administrator rights were asked for and given, so that
    /// an ordinary copy without them could exist beside it.
    /// </summary>
    private static bool HasSplitToken()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();

            // 18: TokenElevationType. 2: the full half of a split token.
            return GetTokenInformation(identity.Token, 18, out int type, sizeof(int), out _) && type == 2;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetTokenInformation(nint token, int informationClass, out int information, int length, out int returned);

    private static bool Check()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
