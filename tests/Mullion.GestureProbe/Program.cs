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

    Win.SetWindowPos(window, 0, work.Left + 220, work.Top + 160, 700, 450, 0x0004);
    Win.SetForegroundWindow(window);
    Thread.Sleep(400);

    // 1. An ordinary drag must behave exactly as it does without Mullion.
    {
        var before = Win.Frame(window);
        var grip = Grip(before);
        Mouse.MoveTo(grip.X, grip.Y);
        Mouse.LeftDown();
        Mouse.Glide(grip.X, grip.Y, grip.X + 120, grip.Y + 70);
        Thread.Sleep(150);
        Mouse.LeftUp();
        Thread.Sleep(400);

        var after = Win.Frame(window);
        Check("Ordinary drag moves the window by the drag distance",
            after, new Win.RECT(before.Left + 120, before.Top + 70, before.Right + 120, before.Bottom + 70));
    }

    // 2. The grid gesture: corner in cell (1,1), stretched to cell (2,2).
    {
        var before = Win.Frame(window);
        var grip = Grip(before);
        var a = (X: work.Left + (w * 3 / 8), Y: work.Top + (h * 3 / 8));
        var b = (X: work.Left + (w * 5 / 8), Y: work.Top + (h * 5 / 8));

        Mouse.MoveTo(grip.X, grip.Y);
        Mouse.LeftDown();
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

        Mouse.MoveTo(grip.X, grip.Y);
        Mouse.LeftDown();
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

        Mouse.MoveTo(grip.X, grip.Y);
        Mouse.LeftDown();
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

        Mouse.MoveTo(grip.X, grip.Y);
        Mouse.LeftDown();
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
finally
{
    Keyboard.Up(snapKey);
    target.Close();
}

Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) failed.");
return failures;

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

/// <summary>An ordinary resizable window on its own thread, standing in for any application.</summary>
internal sealed class TargetWindow
{
    private readonly ManualResetEventSlim _ready = new();
    private Form? _form;

    public TargetWindow()
    {
        var thread = new Thread(() =>
        {
            _form = new Form
            {
                Text = "Mullion probe target",
                StartPosition = FormStartPosition.Manual,
                Location = new System.Drawing.Point(300, 200),
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
    private static extern nint MonitorFromPoint(POINT point, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(nint window, int attribute, out RECT value, int size);

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
