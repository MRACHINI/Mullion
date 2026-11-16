// Drives the real mouse to check Mullion end to end against a window of its own.
//
// Mullion must already be running with its default settings (4 x 4 grid, no gaps,
// Ctrl as the move-on-grid key). The probe takes over the mouse for a few seconds:
//
//   dotnet run --project tests/Mullion.GestureProbe
//
// The exit code is the number of failed checks.

using System.Runtime.InteropServices;
using System.Windows.Forms;

Win.SetProcessDpiAwarenessContext(-4);

// The move-on-grid key to hold in the last check: ctrl (default), shift or alt.
// It must match Mullion's setting; another key helps when a second tool reacts to Ctrl.
ushort snapKey = (args.Length > 0 ? args[0].ToLowerInvariant() : "ctrl") switch
{
    "shift" => 0x10,
    "alt" => 0x12,
    _ => 0x11,
};

int failures = 0;
var target = new TargetWindow();

try
{
    nint window = target.Handle;
    if (window == 0)
    {
        Console.WriteLine("FAIL  The test window did not appear.");
        return 1;
    }

    var work = Win.WorkArea();
    int w = work.Right - work.Left;
    int h = work.Bottom - work.Top;
    Console.WriteLine($"Work area: {work.Left},{work.Top} {w} x {h}");

    // -1 puts it above every other window, including other always-on-top ones.
    Win.SetWindowPos(window, -1, work.Left + 220, work.Top + 160, 700, 450, 0);
    Win.SetForegroundWindow(window);
    Thread.Sleep(400);

    // "arrange" checks the walk-through: the shortcut starts it, clicking a zone
    // places the first window (this one, as it is on top), and the shortcut ends it.
    // Mullion must be running with tests/Mullion.GestureProbe/zones.settings.json.
    if (args.Length > 0 && args[0] == "arrange")
    {
        const ushort F10 = 0x79;
        var rightHalf = new Win.RECT(work.Left + (w / 2), work.Top, work.Right, work.Bottom);

        // A point that is inside the "Right half" zone and no smaller zone.
        int x = work.Left + (w * 3 / 4);
        int y = work.Top + (h * 4 / 5);

        var frame = Win.Frame(window);
        Press((frame.Left + frame.Right) / 2, (frame.Top + frame.Bottom) / 2);
        Mouse.LeftUp();
        Thread.Sleep(300);

        // Which programs own the front-most windows; the test window should come first.
        Console.WriteLine("Front-most windows: " + string.Join(", ", Win.FrontPrograms(6)));

        Shortcut(F10);
        Thread.Sleep(1500);
        CheckThat("The shortcut shows the zones over the screen", Win.ProgramAt(x, y) == "Mullion");

        if (Win.ProgramAt(x, y) == "Mullion")
        {
            Mouse.MoveTo(x, y);
            Thread.Sleep(200);
            Mouse.LeftDown();
            Mouse.LeftUp();
            Thread.Sleep(900);
            Check("Clicking a zone puts the first window there", Win.Frame(window), rightHalf);

            Keyboard.Chord(F10);
            Thread.Sleep(900);
            CheckThat("The shortcut again ends arranging", Win.ProgramAt(x, y) != "Mullion");
        }

        // With Mullion's trace log as second argument: check that the window being
        // placed is brought in front of a window that covers it. Two ordinary
        // windows are opened, one covering the other; the walk-through is skipped
        // forward until the covered one is the current window.
        if (args.Length > 1 && failures == 0)
        {
            var first = new TargetWindow("Mullion probe first", topMost: false, x: work.Left + 900, y: work.Top + 300);
            var second = new TargetWindow("Mullion probe second", topMost: false, x: work.Left + 940, y: work.Top + 340);
            Thread.Sleep(500);

            try
            {
                // Whichever is further back is the one that must come forward.
                bool firstIsBehind = Win.ZIndex(first.Handle) > Win.ZIndex(second.Handle);
                nint behind = firstIsBehind ? first.Handle : second.Handle;
                nint cover = firstIsBehind ? second.Handle : first.Handle;

                frame = Win.Frame(window);
                Press((frame.Left + frame.Right) / 2, (frame.Top + frame.Bottom) / 2);
                Mouse.LeftUp();
                Thread.Sleep(300);
                int shownBefore = ProbeWindowsShown(args[1]);

                Shortcut(F10);
                Thread.Sleep(1500);

                // The always-on-top test window comes first, then the two new ones
                // front to back; the third of the probe's windows is the covered one.
                for (int skip = 0; skip < 12 && ProbeWindowsShown(args[1]) - shownBefore < 3; skip++)
                {
                    if (Win.ProgramAt(x, y) != "Mullion")
                    {
                        break;
                    }

                    Mouse.MoveTo(x, y);
                    Thread.Sleep(100);
                    Mouse.RightDown();
                    Thread.Sleep(60);
                    Mouse.RightUp();
                    Thread.Sleep(500);
                }

                bool reached = ProbeWindowsShown(args[1]) - shownBefore >= 3;
                CheckThat("Skipping reaches the covered window", reached);
                if (reached)
                {
                    CheckThat(
                        "The window being placed is brought in front of the one that covered it",
                        Win.ZIndex(behind) < Win.ZIndex(cover));
                }

                Keyboard.Chord(F10);
                Thread.Sleep(900);
                if (reached)
                {
                    CheckThat(
                        "Ending puts the windows back in their original front-to-back order",
                        Win.ZIndex(cover) < Win.ZIndex(behind));
                }
            }
            finally
            {
                first.Close();
                second.Close();
            }
        }

        Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
        return failures;
    }

    // "zones" checks keyboard shortcuts instead of mouse gestures. Mullion must be
    // running with the two zones listed in tests/Mullion.GestureProbe/zones.settings.json.
    if (args.Length > 0 && args[0] == "zones")
    {
        const ushort F11 = 0x7A, F12 = 0x7B;
        var rightHalf = new Win.RECT(work.Left + (w / 2), work.Top, work.Right, work.Bottom);
        var leftThird = new Win.RECT(work.Left, work.Top, work.Left + (w / 3), work.Bottom);

        // Click inside the window so it is the active one.
        var frame = Win.Frame(window);
        Press((frame.Left + frame.Right) / 2, (frame.Top + frame.Bottom) / 2);
        Mouse.LeftUp();
        Thread.Sleep(300);

        Shortcut(F11);
        Thread.Sleep(600);
        Check("Shortcut moves the active window to the right half", Win.Frame(window), rightHalf);

        Shortcut(F12);
        Thread.Sleep(600);
        Check("A second shortcut moves it to the left third", Win.Frame(window), leftThird);

        var monitors = Win.WorkAreas();
        Console.WriteLine($"Monitors: {monitors.Count}");
        Shortcut(F12);
        Thread.Sleep(600);
        if (monitors.Count > 1)
        {
            int here = monitors.FindIndex(m => m.Equals(work));
            var next = monitors[(here + 1) % monitors.Count];
            var expected = new Win.RECT(next.Left, next.Top, next.Left + ((next.Right - next.Left) / 3), next.Bottom);
            Check("Pressing the shortcut again moves it to the next monitor", Win.Frame(window), expected);

            for (int i = 1; i < monitors.Count; i++)
            {
                Shortcut(F12);
                Thread.Sleep(600);
            }

            Check("Pressing it once per monitor brings it back", Win.Frame(window), leftThird);
        }
        else
        {
            Check("Pressing the shortcut again leaves it in place on a single monitor", Win.Frame(window), leftThird);
        }

        Win.ShowWindow(window, 3);
        Thread.Sleep(500);
        Shortcut(F11);
        Thread.Sleep(900);
        Check("A maximized window is restored and placed", Win.Frame(window), rightHalf);

        Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
        return failures;
    }

    // 1. An ordinary drag must behave exactly as it does without Mullion.
    {
        var before = Win.Frame(window);
        var grip = Grip(before);
        Press(grip.X, grip.Y);
        Mouse.Glide(grip.X, grip.Y, grip.X + 120, grip.Y + 70);
        Thread.Sleep(150);
        Mouse.LeftUp();
        Thread.Sleep(400);

        // Windows itself decides exactly when a drag starts, so allow a few pixels.
        // What matters is that the window went where the mouse went and kept its size.
        var after = Win.Frame(window);
        bool moved = Math.Abs(after.Left - (before.Left + 120)) <= 5 && Math.Abs(after.Top - (before.Top + 70)) <= 5;
        bool sameSize = after.Right - after.Left == before.Right - before.Left && after.Bottom - after.Top == before.Bottom - before.Top;
        CheckThat("Ordinary drag moves the window by the drag distance and keeps its size", moved && sameSize);
        if (!(moved && sameSize))
        {
            Console.WriteLine($"        before {before}");
            Console.WriteLine($"        after  {after}");
        }
    }

    // 2. The grid gesture: corner in cell (1,1), stretched to cell (2,2).
    {
        var before = Win.Frame(window);
        var grip = Grip(before);
        var a = (X: work.Left + (w * 3 / 8), Y: work.Top + (h * 3 / 8));
        var b = (X: work.Left + (w * 5 / 8), Y: work.Top + (h * 5 / 8));

        Press(grip.X, grip.Y);
        Mouse.Glide(grip.X, grip.Y, grip.X + 60, grip.Y + 40);
        Thread.Sleep(200);
        Mouse.RightDown();
        Thread.Sleep(120);
        Mouse.Glide(grip.X + 60, grip.Y + 40, a.X, a.Y);
        Thread.Sleep(120);
        Mouse.RightUp();
        Thread.Sleep(120);
        Mouse.Glide(a.X, a.Y, b.X, b.Y);
        Thread.Sleep(150);

        // Optional second argument: a file to save a half-size picture of the grid to.
        if (args.Length > 1)
        {
            Thread.Sleep(300);
            using var full = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(full))
            {
                g.CopyFromScreen(work.Left, work.Top, 0, 0, full.Size);
            }

            using var half = new System.Drawing.Bitmap(full, w / 2, h / 2);
            half.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
        }

        Mouse.LeftUp();
        Thread.Sleep(700);

        var expected = new Win.RECT(work.Left + (w / 4), work.Top + (h / 4), work.Left + (w * 3 / 4), work.Top + (h * 3 / 4));
        Check("Grid gesture places the window on cells (1,1) to (2,2)", Win.Frame(window), expected);
    }

    // 3. A quick right-click with no movement puts the window on the one cell under the cursor.
    {
        var before = Win.Frame(window);
        var grip = Grip(before);
        var cell = (X: work.Left + (w / 8), Y: work.Top + (h / 8));

        Press(grip.X, grip.Y);
        Mouse.Glide(grip.X, grip.Y, cell.X, cell.Y);
        Thread.Sleep(200);
        Mouse.RightDown();
        Thread.Sleep(60);
        Mouse.RightUp();
        Thread.Sleep(150);
        Mouse.LeftUp();
        Thread.Sleep(700);

        var expected = new Win.RECT(work.Left, work.Top, work.Left + (w / 4), work.Top + (h / 4));
        Check("Right-click then release places the window on the cell under the cursor", Win.Frame(window), expected);
    }

    // 4. Esc abandons the gesture and leaves the window where the drag started.
    {
        var before = Win.Frame(window);
        var grip = Grip(before);

        Press(grip.X, grip.Y);
        Mouse.Glide(grip.X, grip.Y, grip.X + 300, grip.Y + 200);
        Thread.Sleep(200);
        Mouse.RightDown();
        Thread.Sleep(60);
        Mouse.RightUp();
        Thread.Sleep(500);
        Keyboard.Tap(0x1B);
        Thread.Sleep(200);
        Mouse.LeftUp();
        Thread.Sleep(700);

        Check("Esc cancels and the window stays where the drag started", Win.Frame(window), before);
    }

    // 5. The move-on-grid key while dragging snaps the window to the grid without resizing it.
    {
        Win.SetWindowPos(window, 0, work.Left + 100, work.Top + 100, 700, 450, 0x0004);
        Thread.Sleep(300);
        var before = Win.Frame(window);
        var grip = Grip(before);

        // Drag so the top-left corner ends 30 px right and 20 px below the (1,1) grid corner.
        int dx = work.Left + (w / 4) + 30 - before.Left;
        int dy = work.Top + (h / 4) + 20 - before.Top;

        Press(grip.X, grip.Y);
        Mouse.Glide(grip.X, grip.Y, grip.X + 20, grip.Y + 20);
        Keyboard.Down(snapKey);
        Thread.Sleep(100);
        Mouse.Glide(grip.X + 20, grip.Y + 20, grip.X + dx, grip.Y + dy);
        Thread.Sleep(200);
        Mouse.LeftUp();
        Thread.Sleep(200);
        Keyboard.Up(snapKey);
        Thread.Sleep(600);

        int width = before.Right - before.Left;
        int height = before.Bottom - before.Top;
        int x = work.Left + (w / 4);
        int y = work.Top + (h / 4);
        Check("Dragging with the key held snaps to the grid and keeps the size", Win.Frame(window), new Win.RECT(x, y, x + width, y + height));
    }
}
catch (ProbeAbortedException ex)
{
    Mouse.LeftUp();
    Console.WriteLine($"ABORTED  {ex.Message}");
    return 100;
}
finally
{
    Keyboard.Up(snapKey);
    target.Close();
}

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures;

// Moves to a point and presses the left button, but only if the test window is
// what is under the cursor. The probe must never click in anyone else's window.
void Press(int x, int y)
{
    Mouse.MoveTo(x, y);
    Thread.Sleep(60);
    nint under = Win.RootWindowAt(x, y);
    if (under != target.Handle)
    {
        throw new ProbeAbortedException(
            $"Another window ({Win.Describe(under)}) is covering the test window at {x},{y}, so nothing was clicked.");
    }

    Mouse.LeftDown();
}

// Presses Ctrl+Alt+Shift+key, but only while the test window is the active one,
// so a shortcut can never move some other window.
void Shortcut(ushort key)
{
    if (Win.GetForegroundWindow() != target.Handle)
    {
        throw new ProbeAbortedException("The test window is not the active window, so no shortcut was pressed.");
    }

    Keyboard.Chord(key);
}

// How many times Mullion's trace log says it showed one of this probe's windows.
static int ProbeWindowsShown(string logPath)
{
    if (!File.Exists(logPath))
    {
        return 0;
    }

    // Mullion has the log open for writing, so share it.
    using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(stream);
    int count = 0;
    while (reader.ReadLine() is { } line)
    {
        if (line.Contains("showing window") && line.Contains("Mullion.GestureProbe"))
        {
            count++;
        }
    }

    return count;
}

void CheckThat(string name, bool ok)
{
    if (!ok)
    {
        failures++;
    }

    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
}

void Check(string name, Win.RECT actual, Win.RECT expected)
{
    bool ok = actual.Equals(expected);
    if (!ok)
    {
        failures++;
    }

    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}");
    if (!ok)
    {
        Console.WriteLine($"        expected {expected}");
        Console.WriteLine($"        actual   {actual}");
    }
}

static (int X, int Y) Grip(Win.RECT frame) => (frame.Left + ((frame.Right - frame.Left) / 2), frame.Top + 14);

/// <summary>Thrown when going on would mean clicking or typing into someone else's window.</summary>
internal sealed class ProbeAbortedException(string message) : Exception(message);

/// <summary>An ordinary resizable window on its own thread, standing in for any application.</summary>
internal sealed class TargetWindow
{
    private readonly ManualResetEventSlim _ready = new();
    private Form? _form;

    /// <param name="title">Shown in the title bar.</param>
    /// <param name="topMost">
    /// Always on top, so the simulated mouse can reach it whatever else is open.
    /// </param>
    /// <param name="x">Left edge.</param>
    /// <param name="y">Top edge.</param>
    public TargetWindow(string title = "Mullion probe target", bool topMost = true, int x = 300, int y = 200)
    {
        var thread = new Thread(() =>
        {
            _form = new Form
            {
                Text = title,
                TopMost = topMost,
                ShowInTaskbar = true,
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(x, y),
                Size = new System.Drawing.Size(700, 450),
            };
            _form.Shown += (_, _) =>
            {
                Handle = _form.Handle;
                _ready.Set();
            };
            Application.Run(_form);
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(10));
    }

    public nint Handle { get; private set; }

    public void Close()
    {
        if (_form is { IsDisposed: false } form)
        {
            form.BeginInvoke(form.Close);
        }
    }
}

internal static class Mouse
{
    public static void MoveTo(int x, int y)
    {
        int cx = Win.GetSystemMetrics(0);
        int cy = Win.GetSystemMetrics(1);
        Send(0x8001, (int)Math.Round(x * 65535.0 / (cx - 1)), (int)Math.Round(y * 65535.0 / (cy - 1)));
        Thread.Sleep(8);
    }

    public static void Glide(int x0, int y0, int x1, int y1, int steps = 24)
    {
        for (int i = 1; i <= steps; i++)
        {
            MoveTo(x0 + ((x1 - x0) * i / steps), y0 + ((y1 - y0) * i / steps));
        }
    }

    public static void LeftDown()
    {
        Send(0x0002);

        // Give the window time to start its move; movement before that is not counted.
        Thread.Sleep(250);
    }

    public static void LeftUp() => Send(0x0004);
    public static void RightDown() => Send(0x0008);
    public static void RightUp() => Send(0x0010);

    private static void Send(uint flags, int dx = 0, int dy = 0)
    {
        var input = new Win.INPUT { Type = 0 };
        input.Data.Mouse = new Win.MOUSEINPUT { Dx = dx, Dy = dy, Flags = flags };
        Win.SendInput(1, [input], Marshal.SizeOf<Win.INPUT>());
    }
}

internal static class Keyboard
{
    public static void Down(ushort key) => Send(key, 0);
    public static void Up(ushort key) => Send(key, 2);

    public static void Tap(ushort key)
    {
        Down(key);
        Thread.Sleep(30);
        Up(key);
    }

    /// <summary>Presses Ctrl+Alt+Shift together with a key.</summary>
    public static void Chord(ushort key)
    {
        Down(0x11);
        Down(0x12);
        Down(0x10);
        Thread.Sleep(30);
        Tap(key);
        Thread.Sleep(30);
        Up(0x10);
        Up(0x12);
        Up(0x11);
    }

    private static void Send(ushort key, uint flags)
    {
        var input = new Win.INPUT { Type = 1 };
        input.Data.Keyboard = new Win.KEYBDINPUT { VirtualKey = key, Flags = flags };
        Win.SendInput(1, [input], Marshal.SizeOf<Win.INPUT>());
    }
}

internal static class Win
{
    [StructLayout(LayoutKind.Sequential)]
    public record struct RECT(int Left, int Top, int Right, int Bottom)
    {
        public override readonly string ToString() => $"{Left},{Top} to {Right},{Bottom} ({Right - Left} x {Bottom - Top})";
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
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

    [DllImport("user32.dll")]
    public static extern bool SetProcessDpiAwarenessContext(nint context);

    [DllImport("user32.dll")]
    public static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    public static extern uint SendInput(uint count, [In] INPUT[] inputs, int size);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    public static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, [Out] char[] buffer, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    private delegate bool EnumWindowsProc(nint window, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc proc, nint data);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint window);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLength(nint window);

    /// <summary>How far back a window is among all windows: 0 is the front-most.</summary>
    public static int ZIndex(nint target)
    {
        int index = 0;
        int found = int.MaxValue;
        EnumWindowsProc find = (window, _) =>
        {
            if (window == target)
            {
                found = index;
                return false;
            }

            index++;
            return true;
        };

        EnumWindows(find, 0);
        GC.KeepAlive(find);
        return found;
    }

    /// <summary>The programs owning the front-most visible, titled windows, front first.</summary>
    public static List<string> FrontPrograms(int count)
    {
        var names = new List<string>();
        EnumWindowsProc collect = (window, _) =>
        {
            if (names.Count < count && IsWindowVisible(window) && !IsIconic(window) && GetWindowTextLength(window) > 0)
            {
                GetWindowThreadProcessId(window, out uint processId);
                try
                {
                    names.Add(System.Diagnostics.Process.GetProcessById((int)processId).ProcessName);
                }
                catch (ArgumentException)
                {
                    names.Add("?");
                }
            }

            return true;
        };

        EnumWindows(collect, 0);
        GC.KeepAlive(collect);
        return names;
    }

    /// <summary>The name of the program whose window is under a screen point.</summary>
    public static string ProgramAt(int x, int y)
    {
        GetWindowThreadProcessId(RootWindowAt(x, y), out uint processId);
        try
        {
            return System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    /// <summary>The kind of window and the program that owns it, for messages.</summary>
    public static string Describe(nint window)
    {
        var buffer = new char[256];
        int length = GetClassName(window, buffer, buffer.Length);
        GetWindowThreadProcessId(window, out uint processId);
        string program;
        try
        {
            program = System.Diagnostics.Process.GetProcessById((int)processId).ProcessName;
        }
        catch (ArgumentException)
        {
            program = "unknown program";
        }

        return $"{program}, class {new string(buffer, 0, Math.Max(0, length))}";
    }

    /// <summary>The top-level window under a screen point.</summary>
    public static nint RootWindowAt(int x, int y)
    {
        nint window = WindowFromPoint(new POINT { X = x, Y = y });
        nint root = window == 0 ? 0 : GetAncestor(window, 2);
        return root == 0 ? window : root;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out RECT value, int size);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(nint window, int command);

    private delegate bool MonitorEnumProc(nint monitor, nint deviceContext, ref RECT rect, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint deviceContext, nint clip, MonitorEnumProc proc, nint data);

    /// <summary>The work areas of all monitors, left to right, then top to bottom.</summary>
    public static List<RECT> WorkAreas()
    {
        var areas = new List<RECT>();
        MonitorEnumProc collect = (nint monitor, nint _, ref RECT _, nint _) =>
        {
            var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                areas.Add(info.Work);
            }

            return true;
        };

        EnumDisplayMonitors(0, 0, collect, 0);
        GC.KeepAlive(collect);
        return areas.OrderBy(a => a.Left).ThenBy(a => a.Top).ToList();
    }

    /// <summary>The work area of the primary monitor.</summary>
    public static RECT WorkArea()
    {
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(default, 1), ref info);
        return info.Work;
    }

    /// <summary>The visible bounds of a window.</summary>
    public static RECT Frame(nint window)
    {
        DwmGetWindowAttribute(window, 9, out RECT frame, Marshal.SizeOf<RECT>());
        return frame;
    }
}
