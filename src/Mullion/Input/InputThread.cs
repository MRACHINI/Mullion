using System.Runtime.InteropServices;
using System.Windows.Threading;
using Mullion.Core;
using Mullion.Interop;

namespace Mullion.Hooks;

/// <summary>
/// Owns the system-wide mouse hook and the window-move notifications, and feeds
/// them to the <see cref="GestureMachine"/>.
///
/// It runs on its own thread: Windows calls the mouse hook for every mouse event
/// on the whole system, so the hook must never wait behind the user interface.
/// </summary>
internal sealed class InputThread : IGestureHost, IDisposable
{
    /// <summary>How long to wait for the system to report the end of a move before carrying on.</summary>
    private const uint FallbackMs = 300;

    private const int CancelHotkeyId = 0x4D55;

    /// <summary>Marks the keystroke this app injects, so it can be told apart from the user's.</summary>
    private static readonly nint InjectedMarker = 0x4D554C4C;

    private readonly Dispatcher _dispatcher;
    private readonly Action<OverlayModel?> _applyOverlay;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();

    // Kept in fields so the garbage collector cannot free them while Windows still calls them.
    private readonly Native.LowLevelMouseProc _mouseProc;
    private readonly Native.WinEventProc _eventProc;

    private uint _threadId;
    private nint _mouseHook;
    private nint _eventHook;
    private nuint _timerId;
    private bool _cancelKeyRegistered;

    private OverlayModel? _latestOverlay;
    private int _overlayScheduled;
    private volatile int _snapModifierKey = Native.VK_CONTROL;

    public InputThread(Dispatcher dispatcher, Action<OverlayModel?> applyOverlay)
    {
        _dispatcher = dispatcher;
        _applyOverlay = applyOverlay;
        _mouseProc = MouseProc;
        _eventProc = EventProc;
        Machine = new GestureMachine(this);
        _thread = new Thread(Run) { IsBackground = true, Name = "Mullion input" };
    }

    public GestureMachine Machine { get; }

    /// <summary>The virtual-key code of the snap-move modifier, or 0 for none.</summary>
    public int SnapModifierKey
    {
        get => _snapModifierKey;
        set => _snapModifierKey = value;
    }

    /// <summary>Starts the thread and returns once the hooks are installed.</summary>
    public bool Start()
    {
        _thread.Start();
        _started.Wait();
        return _mouseHook != 0 && _eventHook != 0;
    }

    public void Dispose()
    {
        if (_threadId != 0 && _thread.IsAlive)
        {
            Native.PostThreadMessage(_threadId, Native.WM_QUIT, 0, 0);
            _thread.Join(2000);
        }

        _started.Dispose();
    }

    private void Run()
    {
        try
        {
            _threadId = Native.GetCurrentThreadId();
            _mouseHook = Native.SetWindowsHookEx(Native.WH_MOUSE_LL, _mouseProc, Native.GetModuleHandle(null), 0);
            if (_mouseHook == 0)
            {
                Log.Write($"Could not install the mouse hook (error {Marshal.GetLastWin32Error()}).");
            }

            _eventHook = Native.SetWinEventHook(
                Native.EVENT_SYSTEM_MOVESIZESTART,
                Native.EVENT_SYSTEM_MOVESIZEEND,
                0,
                _eventProc,
                0,
                0,
                Native.WINEVENT_OUTOFCONTEXT);
            if (_eventHook == 0)
            {
                Log.Write("Could not subscribe to window move notifications.");
            }
        }
        finally
        {
            _started.Set();
        }

        try
        {
            while (Native.GetMessage(out var message, 0, 0, 0) > 0)
            {
                try
                {
                    if (message.Message == Native.WM_HOTKEY && message.WParam == CancelHotkeyId)
                    {
                        Machine.OnCancelKey();
                    }
                    else if (message.Message == Native.WM_TIMER && _timerId != 0 && (nuint)message.WParam == _timerId)
                    {
                        Machine.OnTimer();
                    }
                    else
                    {
                        Native.DispatchMessage(ref message);
                    }
                }
                catch (Exception ex)
                {
                    Log.Write("Input thread message failed.", ex);
                }
            }
        }
        finally
        {
            SetCancelKeyEnabled(false);
            SetTimer(false);

            if (_mouseHook != 0)
            {
                Native.UnhookWindowsHookEx(_mouseHook);
            }

            if (_eventHook != 0)
            {
                Native.UnhookWinEvent(_eventHook);
            }
        }
    }

    private nint MouseProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            try
            {
                // MSLLHOOKSTRUCT starts with the point (x, y) followed by the wheel data.
                var point = new PointI(Marshal.ReadInt32(lParam), Marshal.ReadInt32(lParam, 4));
                bool hide = (int)wParam switch
                {
                    Native.WM_MOUSEMOVE => Machine.OnMove(point),
                    Native.WM_LBUTTONDOWN => Machine.OnLeftDown(point),
                    Native.WM_LBUTTONUP => Machine.OnLeftUp(point),
                    Native.WM_RBUTTONDOWN => Machine.OnRightDown(point),
                    Native.WM_RBUTTONUP => Machine.OnRightUp(point),
                    Native.WM_MOUSEWHEEL => Machine.OnWheel(point, (short)(Marshal.ReadInt32(lParam, 8) >> 16)),
                    _ => false,
                };

                if (hide)
                {
                    return 1;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Mouse hook failed.", ex);
            }
        }

        return Native.CallNextHookEx(0, code, wParam, lParam);
    }

    private void EventProc(
        nint hook, uint eventType, nint window, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        if (window == 0 || idObject != Native.OBJID_WINDOW)
        {
            return;
        }

        try
        {
            nint root = Native.GetAncestor(window, Native.GA_ROOT);
            if (root == 0)
            {
                root = window;
            }

            if (eventType == Native.EVENT_SYSTEM_MOVESIZESTART)
            {
                Machine.OnMoveSizeStart(root);
            }
            else if (eventType == Native.EVENT_SYSTEM_MOVESIZEEND)
            {
                Machine.OnMoveSizeEnd(root);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Window move notification failed.", ex);
        }
    }

    RectI IGestureHost.WorkAreaAt(PointI point) => Native.WorkAreaAt(point);

    RectI? IGestureHost.GetFrameBounds(nint window) => Native.GetFrameBounds(window);

    bool IGestureHost.IsSnapModifierDown()
    {
        int key = _snapModifierKey;
        return key != 0 && (Native.GetAsyncKeyState(key) & 0x8000) != 0;
    }

    void IGestureHost.CancelSystemMove()
    {
        // Escape is how Windows itself cancels a window move: the window goes back to
        // where the drag started. It is sent straight away so it reaches the move
        // before the user can release the button.
        var inputs = new Native.INPUT[2];
        inputs[0].Type = Native.INPUT_KEYBOARD;
        inputs[0].Data.Keyboard = new Native.KEYBDINPUT { VirtualKey = Native.VK_ESCAPE, ExtraInfo = InjectedMarker };
        inputs[1].Type = Native.INPUT_KEYBOARD;
        inputs[1].Data.Keyboard = new Native.KEYBDINPUT
        {
            VirtualKey = Native.VK_ESCAPE,
            Flags = Native.KEYEVENTF_KEYUP,
            ExtraInfo = InjectedMarker,
        };

        if (Native.SendInput(2, inputs, Marshal.SizeOf<Native.INPUT>()) != 2)
        {
            Log.Write($"Could not cancel the system move (error {Marshal.GetLastWin32Error()}).");
        }
    }

    public void SetCancelKeyEnabled(bool enabled)
    {
        if (enabled == _cancelKeyRegistered)
        {
            return;
        }

        if (enabled)
        {
            _cancelKeyRegistered = Native.RegisterHotKey(0, CancelHotkeyId, Native.MOD_NOREPEAT, Native.VK_ESCAPE);
        }
        else
        {
            Native.UnregisterHotKey(0, CancelHotkeyId);
            _cancelKeyRegistered = false;
        }
    }

    public void SetTimer(bool enabled)
    {
        if (_timerId != 0)
        {
            Native.KillTimer(0, _timerId);
            _timerId = 0;
        }

        if (enabled)
        {
            _timerId = Native.SetTimer(0, 0, FallbackMs, 0);
        }
    }

    void IGestureHost.ShowOverlay(OverlayModel model) => PostOverlay(model);

    void IGestureHost.HideOverlay() => PostOverlay(null);

    void IGestureHost.Place(nint window, RectI bounds, bool keepSize)
    {
        // Moving a window waits for the application that owns it. Do that away from
        // this thread so a slow application cannot make the mouse stutter.
        Task.Run(() =>
        {
            try
            {
                WindowPlacer.Place(window, bounds, keepSize);
            }
            catch (Exception ex)
            {
                Log.Write("Placing a window failed.", ex);
            }
        });
    }

    /// <summary>Hands the newest overlay state to the user interface, skipping any it has not drawn yet.</summary>
    private void PostOverlay(OverlayModel? model)
    {
        Volatile.Write(ref _latestOverlay, model);

        if (Interlocked.Exchange(ref _overlayScheduled, 1) == 0)
        {
            _dispatcher.BeginInvoke(DispatcherPriority.Send, () =>
            {
                Interlocked.Exchange(ref _overlayScheduled, 0);
                _applyOverlay(Volatile.Read(ref _latestOverlay));
            });
        }
    }
}
