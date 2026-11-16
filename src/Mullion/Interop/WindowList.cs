namespace Mullion.Interop;

/// <summary>Finds the open windows a person would think of as "my windows".</summary>
internal static class WindowList
{
    /// <summary>Windows that belong to the desktop and taskbar rather than to a program.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame",
    };

    public sealed record Entry(nint Handle, string Title);

    public static bool IsShellWindow(nint window) => ShellClasses.Contains(Native.GetClassName(window));

    /// <summary>The name of the program that owns a window, or "?" when it cannot be found.</summary>
    public static string ProgramOf(nint window)
    {
        Native.GetWindowThreadProcessId(window, out uint processId);
        if (processId == 0)
        {
            return "?";
        }

        if (Native.GetProgramName(processId) is { } name)
        {
            return name;
        }

        // The quick way is refused for some protected programs; this slower one
        // still knows their names.
        try
        {
            return System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return "?";
        }
    }

    private static volatile HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Sets the programs Mullion is to leave alone. Every part of the app asks
    /// <see cref="IsExcluded"/>, so one list covers the gesture, shortcuts,
    /// arranging and rules. Safe to call from any thread.
    /// </summary>
    public static void SetExcludedPrograms(IEnumerable<string> programs)
    {
        _excluded = new HashSet<string>(programs, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True when the window belongs to a program Mullion is to leave alone.</summary>
    public static bool IsExcluded(nint window)
    {
        var excluded = _excluded;
        return excluded.Count > 0 && excluded.Contains(ProgramOf(window));
    }

    /// <summary>
    /// The windows that can be arranged, front-most first: visible, not minimized,
    /// with a title, shown in the taskbar, and not part of Mullion itself. This is
    /// roughly what Alt+Tab lists.
    /// </summary>
    /// <param name="includeExcluded">
    /// Also list windows of programs Mullion is to leave alone; used where the user
    /// chooses which programs those are.
    /// </param>
    public static List<Entry> GetArrangeable(bool includeExcluded = false)
    {
        var result = new List<Entry>();
        uint ownProcess = (uint)Environment.ProcessId;

        Native.EnumWindowsProc collect = (window, _) =>
        {
            if (IsArrangeable(window, ownProcess, includeExcluded))
            {
                result.Add(new Entry(window, Native.GetWindowTitle(window)));
            }

            return true;
        };

        Native.EnumWindows(collect, 0);
        GC.KeepAlive(collect);
        return result;
    }

    /// <summary>True while a window found earlier is still there and still showing.</summary>
    public static bool IsStillArrangeable(nint window) =>
        IsArrangeable(window, (uint)Environment.ProcessId, includeExcluded: false);

    private static bool IsArrangeable(nint window, uint ownProcess, bool includeExcluded)
    {
        if (!Native.IsWindow(window) || !Native.IsWindowVisible(window) || Native.IsIconic(window))
        {
            return false;
        }

        if (Native.IsCloaked(window) || IsShellWindow(window))
        {
            return false;
        }

        Native.GetWindowThreadProcessId(window, out uint process);
        if (process == ownProcess)
        {
            return false;
        }

        // Tool windows and owned windows (dialogs, palettes) do not get a taskbar
        // button unless they ask for one; their main window is the one to arrange.
        long style = Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE);
        bool appWindow = (style & Native.WS_EX_APPWINDOW) != 0;
        bool toolWindow = (style & Native.WS_EX_TOOLWINDOW) != 0;
        bool owned = Native.GetWindow(window, Native.GW_OWNER) != 0;
        if (!appWindow && (toolWindow || owned))
        {
            return false;
        }

        if (Native.GetWindowTitle(window).Length == 0)
        {
            return false;
        }

        // Checked last because it is the slowest test.
        return includeExcluded || !IsExcluded(window);
    }
}
