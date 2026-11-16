using System.Runtime.InteropServices;
using Mullion.Core;

namespace Mullion.Interop;

/// <summary>The Win32 functions, constants and structures the app uses.</summary>
internal static class Native
{
    public const int WH_MOUSE_LL = 14;
    public const int WH_KEYBOARD_LL = 13;

    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_SYSKEYDOWN = 0x0104;
    public const int WM_SYSKEYUP = 0x0105;
    public const int VK_LWIN = 0x5B;
    public const int VK_RWIN = 0x5C;

    /// <summary>A key code no keyboard has; pressing it does nothing in any program.</summary>
    public const ushort VK_UNASSIGNED = 0xE8;

    public const int WM_APP = 0x8000;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_TIMER = 0x0113;
    public const int WM_QUIT = 0x0012;
    public const int WM_MOUSEMOVE = 0x0200;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_MOUSEWHEEL = 0x020A;

    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
    public const uint EVENT_SYSTEM_MOVESIZESTART = 0x000A;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const int OBJID_WINDOW = 0;

    public const uint MONITOR_DEFAULTTONEAREST = 2;
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_THICKFRAME = 0x00040000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const long WS_EX_NOACTIVATE = 0x08000000;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_NOOWNERZORDER = 0x0200;
    public static readonly nint HWND_TOPMOST = -1;

    public const int SW_RESTORE = 9;
    public const uint GA_ROOT = 2;

    public const uint INPUT_KEYBOARD = 1;
    public const uint KEYEVENTF_KEYUP = 0x0002;
    public const ushort VK_ESCAPE = 0x1B;
    public const int VK_SHIFT = 0x10;
    public const int VK_CONTROL = 0x11;
    public const int VK_MENU = 0x12;
    public const uint MOD_NOREPEAT = 0x4000;

    public delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);

    public delegate void WinEventProc(
        nint hook, uint eventType, nint window, int idObject, int idChild, uint eventThread, uint eventTime);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly RectI ToRectI() => new(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public nint Window;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public POINT Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO
    {
        public int Size;
        public RECT Monitor;
        public RECT Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct KEYBDINPUT
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public nint ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    public struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT Mouse;
        [FieldOffset(0)] public KEYBDINPUT Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint Type;
        public INPUTUNION Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern nint SetWindowsHookEx(int id, LowLevelMouseProc proc, nint module, uint threadId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(nint hook);

    [DllImport("user32.dll")]
    public static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    public static extern nint SetWinEventHook(
        uint eventMin, uint eventMax, nint module, WinEventProc proc, uint processId, uint threadId, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(nint hook);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern nint GetModuleHandle(string? moduleName);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG message, nint window, uint filterMin, uint filterMax);

    [DllImport("user32.dll")]
    public static extern nint DispatchMessage(ref MSG message);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(nint window, int id);

    [DllImport("user32.dll")]
    public static extern nuint SetTimer(nint window, nuint id, uint elapseMs, nint callback);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool KillTimer(nint window, nuint id);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, [In] INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    public static extern nint MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(nint window, out RECT rect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(nint window, int attribute, out RECT value, int size);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsZoomed(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(nint window, int command);

    [DllImport("user32.dll")]
    public static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern nint SetWindowLongPtr(nint window, int index, nint value);

    public delegate bool MonitorEnumProc(nint monitor, nint deviceContext, ref RECT rect, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumDisplayMonitors(nint deviceContext, nint clip, MonitorEnumProc proc, nint data);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, [Out] char[] buffer, int maxCount);

    /// <summary>The window class name, which identifies what kind of window it is.</summary>
    public static string GetClassName(nint window)
    {
        var buffer = new char[256];
        int length = GetClassName(window, buffer, buffer.Length);
        return length > 0 ? new string(buffer, 0, length) : string.Empty;
    }

    /// <summary>The work areas of all monitors, ordered left to right, then top to bottom.</summary>
    public static List<RectI> GetWorkAreas()
    {
        var areas = new List<RectI>();
        MonitorEnumProc collect = (nint monitor, nint _, ref RECT _, nint _) =>
        {
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                areas.Add(info.Work.ToRectI());
            }

            return true;
        };

        EnumDisplayMonitors(0, 0, collect, 0);
        GC.KeepAlive(collect);
        return MonitorList.Sort(areas);
    }

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageName(nint process, uint flags, [Out] char[] name, ref uint size);

    /// <summary>
    /// The name of a running program without its folder or ".exe", as Task Manager
    /// shows it; null when it cannot be read. Quick enough to call when a drag starts.
    /// </summary>
    public static string? GetProgramName(uint processId)
    {
        nint process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process == 0)
        {
            return null;
        }

        try
        {
            var buffer = new char[1024];
            uint size = (uint)buffer.Length;
            if (!QueryFullProcessImageName(process, 0, buffer, ref size) || size == 0)
            {
                return null;
            }

            string path = new(buffer, 0, (int)size);
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            return name.Length > 0 ? name : null;
        }
        finally
        {
            CloseHandle(process);
        }
    }

    public const int DWMWA_CLOAKED = 14;
    public const uint GW_OWNER = 4;
    public const long WS_EX_APPWINDOW = 0x00040000;
    public static readonly nint HWND_TOP = 0;
    public static readonly nint HWND_NOTOPMOST = -2;
    public const long WS_EX_TOPMOST = 0x00000008;

    public delegate bool EnumWindowsProc(nint window, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumWindows(EnumWindowsProc proc, nint data);

    [DllImport("user32.dll")]
    public static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT point);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out int value, int size);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, [Out] char[] buffer, int maxCount);

    /// <summary>The text in a window's title bar.</summary>
    public static string GetWindowTitle(nint window)
    {
        int length = GetWindowTextLength(window);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        int copied = GetWindowText(window, buffer, buffer.Length);
        return copied > 0 ? new string(buffer, 0, copied) : string.Empty;
    }

    /// <summary>
    /// True for a window that exists but that Windows is not showing, such as one on
    /// another virtual desktop or a suspended Store app.
    /// </summary>
    public static bool IsCloaked(nint window) =>
        DwmGetWindowAttribute(window, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;

    /// <summary>
    /// True when a shortcut cannot be registered right now, which means some
    /// program already owns it. The shortcut is released again straight away.
    /// </summary>
    public static bool IsHotkeyTaken(Hotkey hotkey)
    {
        const int ProbeId = 0xB000;
        if (RegisterHotKey(0, ProbeId, (uint)hotkey.Modifiers, (uint)hotkey.Key))
        {
            UnregisterHotKey(0, ProbeId);
            return false;
        }

        return true;
    }

    /// <summary>The usable area, without the taskbar, of the monitor nearest to a point.</summary>
    public static RectI WorkAreaAt(PointI point)
    {
        nint monitor = MonitorFromPoint(new POINT { X = point.X, Y = point.Y }, MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        return monitor != 0 && GetMonitorInfo(monitor, ref info) ? info.Work.ToRectI() : default;
    }

    /// <summary>
    /// The bounds of a window as the user sees it. On Windows 10 and 11 the window
    /// rectangle also includes invisible resize borders, which this leaves out.
    /// </summary>
    public static RectI? GetFrameBounds(nint window)
    {
        if (DwmGetWindowAttribute(window, DWMWA_EXTENDED_FRAME_BOUNDS, out RECT frame, Marshal.SizeOf<RECT>()) == 0)
        {
            return frame.ToRectI();
        }

        return GetWindowRect(window, out RECT rect) ? rect.ToRectI() : null;
    }
}
