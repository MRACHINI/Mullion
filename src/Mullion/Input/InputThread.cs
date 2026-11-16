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

    private const int CancelHotkeyId = 1;

    /// <summary>Zone shortcuts use this id plus the zone's position in the list.</summary>
    private const int ZoneHotkeyBase = 0x100;

    /// <summary>Posted to this thread to make it register the current set of shortcuts.</summary>
    private const uint ApplyHotkeysMessage = Native.WM_APP + 1;

    /// <summary>Marks the keystroke this app injects, so it can be told apart from the user's.</summary>
    private static readonly nint InjectedMarker = 0x4D554C4C;

    private readonly Dispatcher _dispatcher;
    private readonly Action<OverlayModel?> _applyOverlay;
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _started = new();

    // Kept in fields so the garbage collector cannot free them while Windows still calls them.
    private readonly Native.LowLevelMouseProc _mouseProc;
    private readonly Native.LowLevelMouseProc _keyboardProc;
    private readonly Native.WinEventProc _eventProc;

    // The keyboard hook is installed only while it is needed: for shortcuts another
    // program owns, and while a new shortcut is being recorded.
    private readonly HotkeyMatcher _matcher = new();
    private nint _keyboardHook;
    private volatile Action<Hotkey>? _recorder;

    // Watching for windows that open is switched on only while placement rules need it.
    private readonly Native.WinEventProc _shownProc;
    private nint _shownHook;
    private nint _uncloakedHook;
    private volatile bool _watchWindows;

    private uint _threadId;
    private nint _mouseHook;
    private nint _eventHook;
    private nuint _timerId;
    private bool _cancelKeyRegistered;

    private OverlayModel? _latestOverlay;
    private int _overlayScheduled;
    private volatile int _snapModifierKey = Native.VK_CONTROL;

    private readonly Action<int> _zonePressed;
    private readonly Action<IReadOnlyList<int>> _hotkeyProblems;
    private readonly List<int> _registeredHotkeys = [];
    private volatile Hotkey?[] _zoneHotkeys = [];
    private volatile bool _hotkeysSuspended;

    /// <param name="dispatcher">The user interface thread.</param>
    /// <param name="applyOverlay">Called on the user interface thread with the overlay to show, or null.</param>
    /// <param name="zonePressed">Called on a worker thread with the position of the zone whose shortcut was pressed.</param>
    /// <param name="hotkeyProblems">
    /// Called on the user interface thread with the zones whose shortcut another
    /// program owns and Mullion has therefore taken over.
    /// </param>
    public InputThread(
        Dispatcher dispatcher,
        Action<OverlayModel?> applyOverlay,
        Action<int> zonePressed,
        Action<IReadOnlyList<int>> hotkeyProblems)
    {
        _dispatcher = dispatcher;
        _applyOverlay = applyOverlay;
        _zonePressed = zonePressed;
        _hotkeyProblems = hotkeyProblems;
        _mouseProc = MouseProc;
        _keyboardProc = KeyboardProc;
        _eventProc = EventProc;
        _shownProc = ShownProc;
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

    /// <summary>
    /// Sets the shortcut of each zone, by position in the zone list; null for a zone
    /// without one. Safe to call from any thread.
    /// </summary>
    public void SetZoneHotkeys(IReadOnlyList<Hotkey?> hotkeys)
    {
        _zoneHotkeys = [.. hotkeys];
        RequestHotkeyUpdate();
    }

    /// <summary>
    /// Turns all zone shortcuts off or back on, for example while the app is paused
    /// or while the user is recording a new shortcut.
    /// </summary>
    public void SetHotkeysSuspended(bool suspended)
    {
        _hotkeysSuspended = suspended;
        RequestHotkeyUpdate();
    }

    /// <summary>
    /// Called on this thread with each top-level window that becomes visible, while
    /// watching is on. It runs for every such window on the system, so it must
    /// return at once and do any real work elsewhere.
    /// </summary>
    public Action<nint>? WindowShown { get; set; }

    /// <summary>Turns watching for newly shown windows on or off. Safe to call from any thread.</summary>
    public void SetWindowWatch(bool enabled)
    {
        _watchWindows = enabled;
        RequestHotkeyUpdate();
    }

    /// <summary>
    /// Starts recording a shortcut: the next usable key combination is passed to
    /// <paramref name="captured"/> on the user interface thread instead of reaching
    /// any program. Pass null to stop.
    /// </summary>
    public void SetRecorder(Action<Hotkey>? captured)
    {
        _recorder = captured;
        RequestHotkeyUpdate();
    }

    private void RequestHotkeyUpdate()
    {
        // Shortcuts belong to the thread that registers them, so the work is done there.
        if (_threadId != 0)
        {
            Native.PostThreadMessage(_threadId, ApplyHotkeysMessage, 0, 0);
        }
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

            ApplyHotkeys();
            ApplyWindowWatch();
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
                    else if (message.Message == Native.WM_HOTKEY && message.WParam >= ZoneHotkeyBase)
                    {
                        // Moving a window waits for its application, so do it elsewhere.
                        int zone = (int)message.WParam - ZoneHotkeyBase;
                        Task.Run(() => RunZone(zone));
                    }
                    else if (message.Message == ApplyHotkeysMessage)
                    {
                        ApplyHotkeys();
                        ApplyWindowWatch();
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
            UnregisterZoneHotkeys();
            SetKeyboardHook(false);
            _watchWindows = false;
            ApplyWindowWatch();

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

    private void RunZone(int zone)
    {
        try
        {
            _zonePressed(zone);
        }
        catch (Exception ex)
        {
            Log.Write("A zone shortcut failed.", ex);
        }
    }

    private void UnregisterZoneHotkeys()
    {
        foreach (int id in _registeredHotkeys)
        {
            Native.UnregisterHotKey(0, id);
        }

        _registeredHotkeys.Clear();
    }

    /// <summary>
    /// Registers the current zone shortcuts with Windows. A shortcut Windows refuses
    /// already belongs to another program; those are taken over with the keyboard
    /// hook instead, and reported.
    /// </summary>
    private void ApplyHotkeys()
    {
        UnregisterZoneHotkeys();

        var takenOver = new List<int>();
        var hooked = new Dictionary<Hotkey, int>();

        if (!_hotkeysSuspended)
        {
            var hotkeys = _zoneHotkeys;
            for (int i = 0; i < hotkeys.Length; i++)
            {
                if (hotkeys[i] is not { } hotkey)
                {
                    continue;
                }

                int id = ZoneHotkeyBase + i;
                if (Native.RegisterHotKey(0, id, (uint)hotkey.Modifiers | Native.MOD_NOREPEAT, (uint)hotkey.Key))
                {
                    _registeredHotkeys.Add(id);
                }
                else
                {
                    hooked[hotkey] = i;
                    takenOver.Add(i);
                }
            }
        }

        _matcher.SetZones(hooked);
        _matcher.Recording = _recorder is not null;
        SetKeyboardHook(!_matcher.IsIdle);

        if (!_hotkeysSuspended)
        {
            _dispatcher.BeginInvoke(() => _hotkeyProblems(takenOver));
        }
    }

    /// <summary>Subscribes to, or unsubscribes from, "a window was shown" notifications.</summary>
    private void ApplyWindowWatch()
    {
        bool wanted = _watchWindows;

        if (wanted && _shownHook == 0)
        {
            // Store apps exist before they appear and are "uncloaked" rather than shown.
            uint flags = Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS;
            _shownHook = Native.SetWinEventHook(
                Native.EVENT_OBJECT_SHOW, Native.EVENT_OBJECT_SHOW, 0, _shownProc, 0, 0, flags);
            _uncloakedHook = Native.SetWinEventHook(
                Native.EVENT_OBJECT_UNCLOAKED, Native.EVENT_OBJECT_UNCLOAKED, 0, _shownProc, 0, 0, flags);
            if (_shownHook == 0)
            {
                Log.Write("Could not subscribe to window-opened notifications.");
            }
        }
        else if (!wanted)
        {
            if (_shownHook != 0)
            {
                Native.UnhookWinEvent(_shownHook);
                _shownHook = 0;
            }

            if (_uncloakedHook != 0)
            {
                Native.UnhookWinEvent(_uncloakedHook);
                _uncloakedHook = 0;
            }
        }
    }

    private void ShownProc(
        nint hook, uint eventType, nint window, int idObject, int idChild, uint eventThread, uint eventTime)
    {
        // The notification arrives for every control and menu on the system; only
        // windows themselves are of interest, and the rest is decided elsewhere.
        if (window == 0 || idObject != Native.OBJID_WINDOW || idChild != 0)
        {
            return;
        }

        try
        {
            if (Native.GetAncestor(window, Native.GA_ROOT) == window)
            {
                WindowShown?.Invoke(window);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Window-opened notification failed.", ex);
        }
    }

    private void SetKeyboardHook(bool wanted)
    {
        if (wanted && _keyboardHook == 0)
        {
            _keyboardHook = Native.SetWindowsHookEx(
                Native.WH_KEYBOARD_LL, _keyboardProc, Native.GetModuleHandle(null), 0);
            if (_keyboardHook == 0)
            {
                Log.Write($"Could not install the keyboard hook (error {Marshal.GetLastWin32Error()}).");
            }
        }
        else if (!wanted && _keyboardHook != 0)
        {
            Native.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = 0;
        }
    }

    private nint KeyboardProc(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            try
            {
                // KBDLLHOOKSTRUCT: key code at 0, extra info at 16.
                int key = Marshal.ReadInt32(lParam);
                nint extra = Marshal.ReadIntPtr(lParam, 16);
                int message = (int)wParam;

                // Keys this app injects itself are never shortcuts.
                if (extra != InjectedMarker)
                {
                    if (message is Native.WM_KEYDOWN or Native.WM_SYSKEYDOWN)
                    {
                        var modifiers = HeldModifiers();
                        var decision = _matcher.KeyDown(key, modifiers);

                        if (decision.Captured is { } captured && _recorder is { } recorder)
                        {
                            _dispatcher.BeginInvoke(() => recorder(captured));
                        }

                        if (decision.Zone is { } zone)
                        {
                            Task.Run(() => RunZone(zone));
                        }

                        if (decision.Swallow)
                        {
                            MaskModifiers(modifiers);
                            return 1;
                        }
                    }
                    else if (message is Native.WM_KEYUP or Native.WM_SYSKEYUP && _matcher.KeyUp(key))
                    {
                        return 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Write("Keyboard hook failed.", ex);
            }
        }

        return Native.CallNextHookEx(0, code, wParam, lParam);
    }

    private static HotkeyModifiers HeldModifiers()
    {
        static bool Down(int key) => (Native.GetAsyncKeyState(key) & 0x8000) != 0;

        var modifiers = HotkeyModifiers.None;
        if (Down(Native.VK_CONTROL))
        {
            modifiers |= HotkeyModifiers.Ctrl;
        }

        if (Down(Native.VK_MENU))
        {
            modifiers |= HotkeyModifiers.Alt;
        }

        if (Down(Native.VK_SHIFT))
        {
            modifiers |= HotkeyModifiers.Shift;
        }

        if (Down(Native.VK_LWIN) || Down(Native.VK_RWIN))
        {
            modifiers |= HotkeyModifiers.Win;
        }

        return modifiers;
    }

    /// <summary>
    /// When a combination with Alt or the Windows key is hidden, Windows sees that
    /// modifier pressed and released alone and would open a menu bar or the Start
    /// menu. Pressing a key that does nothing in between prevents that.
    /// </summary>
    private static void MaskModifiers(HotkeyModifiers modifiers)
    {
        if ((modifiers & (HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0)
        {
            return;
        }

        var inputs = new Native.INPUT[2];
        inputs[0].Type = Native.INPUT_KEYBOARD;
        inputs[0].Data.Keyboard = new Native.KEYBDINPUT { VirtualKey = Native.VK_UNASSIGNED, ExtraInfo = InjectedMarker };
        inputs[1].Type = Native.INPUT_KEYBOARD;
        inputs[1].Data.Keyboard = new Native.KEYBDINPUT
        {
            VirtualKey = Native.VK_UNASSIGNED,
            Flags = Native.KEYEVENTF_KEYUP,
            ExtraInfo = InjectedMarker,
        };
        Native.SendInput(2, inputs, Marshal.SizeOf<Native.INPUT>());
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

    /// <summary>
    /// Called on a worker thread after a window was placed on grid cells with the
    /// mouse gesture, with the window and the cells.
    /// </summary>
    public Action<nint, GridPlacement>? GridPlaced { get; set; }

    void IGestureHost.Place(nint window, RectI bounds, bool keepSize, GridPlacement? grid)
    {
        // Moving a window waits for the application that owns it. Do that away from
        // this thread so a slow application cannot make the mouse stutter.
        Task.Run(() =>
        {
            try
            {
                WindowPlacer.Place(window, bounds, keepSize);
                if (grid is { } cells)
                {
                    GridPlaced?.Invoke(window, cells);
                }
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
