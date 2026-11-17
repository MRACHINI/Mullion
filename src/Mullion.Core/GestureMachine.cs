namespace Mullion.Core;

/// <summary>The settings the gesture logic needs. Replaced as a whole when settings change.</summary>
public sealed record GestureConfig(
    int Columns = 4,
    int Rows = 4,
    int WindowGap = 0,
    int EdgeGap = 0,
    bool SnapMoveEnabled = true,
    bool Paused = false,
    TriggerButton GridButton = TriggerButton.Right);

public enum OverlayKind
{
    /// <summary>The grid button is held and the starting corner follows the cursor.</summary>
    Anchor,

    /// <summary>The starting corner is fixed and the cursor sets the opposite corner.</summary>
    Select,

    /// <summary>The window keeps its size and snaps to grid lines while it is dragged.</summary>
    SnapMove,
}

/// <summary>What the overlay should draw.</summary>
public sealed record OverlayModel(
    RectI Area,
    int Columns,
    int Rows,
    RectI Highlight,
    CellSpan? Span,
    OverlayKind Kind);

/// <summary>
/// The grid cells a window was placed on: enough to put another window in the
/// same place later, on any monitor.
/// </summary>
public readonly record struct GridPlacement(RectI Area, int Columns, int Rows, CellSpan Span);

/// <summary>Everything the gesture logic needs from the operating system.</summary>
public interface IGestureHost
{
    /// <summary>The usable area (without the taskbar) of the monitor nearest to a point.</summary>
    RectI WorkAreaAt(PointI point);

    /// <summary>The visible bounds of a window, or null when they cannot be read.</summary>
    RectI? GetFrameBounds(nint window);

    /// <summary>
    /// False for a window the user has told Mullion to leave alone. Asked once when
    /// a drag starts, so it may take a moment but must not block.
    /// </summary>
    bool CanArrange(nint window);

    bool IsSnapModifierDown();

    /// <summary>Ends the system's own move of the dragged window, putting it back where it started.</summary>
    void CancelSystemMove();

    /// <summary>Turns the key that abandons the gesture on or off.</summary>
    void SetCancelKeyEnabled(bool enabled);

    void ShowOverlay(OverlayModel model);
    void HideOverlay();

    /// <summary>Moves a window so its visible bounds match <paramref name="bounds"/>.</summary>
    /// <param name="window">The window to move.</param>
    /// <param name="bounds">Where its visible bounds should end up.</param>
    /// <param name="keepSize">Move only; leave the size alone.</param>
    /// <param name="grid">
    /// The grid cells the user selected, when the placement came from the grid; null
    /// for a move that only snapped to grid lines.
    /// </param>
    void Place(nint window, RectI bounds, bool keepSize, GridPlacement? grid);

    /// <summary>Starts or stops a one-shot timer that calls <see cref="GestureMachine.OnTimer"/>.</summary>
    void SetTimer(bool enabled);
}

/// <summary>
/// Turns raw mouse and window events into grid placements.
///
/// The main gesture: while a window is being dragged with the left button, a click
/// of the grid button (the right button, unless the user chose another) shows the
/// grid. The corner follows the cursor while that button is held and is fixed when
/// it is released; moving then stretches the selection, and releasing the left
/// button places the window.
///
/// All methods must be called from one thread. Mouse methods return true when the
/// event should be hidden from other applications.
/// </summary>
public sealed class GestureMachine
{
    private readonly IGestureHost _host;
    private volatile GestureConfig _config = new();

    private enum Mode
    {
        Idle,
        Anchoring,
        Selecting,
        SnapMove,
    }

    private Mode _mode;
    private bool _leftDown;

    // The button that started the grid now showing; a change of settings in the
    // middle of a placement must not change which button it listens to.
    private TriggerButton _gridButton;

    // The buttons whose press was hidden, one bit each, so that the matching
    // release is hidden too.
    private int _hiddenButtons;
    private bool _systemMoveEnded;
    private nint _moving;
    private nint _target;
    private PointI _last;
    private PointI _anchor;
    private RectI _area;
    private int _density;

    // Wheel movement not yet turned into a step.
    private int _wheel;
    private const int WheelNotch = 120;
    private OverlayModel? _shown;
    private (nint Window, RectI Bounds, bool KeepSize, GridPlacement? Grid)? _pending;

    public GestureMachine(IGestureHost host)
    {
        _host = host;
    }

    /// <summary>Safe to set from any thread.</summary>
    public GestureConfig Config
    {
        get => _config;
        set => _config = value;
    }

    private bool InGridMode => _mode is Mode.Anchoring or Mode.Selecting;

    public bool OnLeftDown(PointI point)
    {
        _last = point;
        _leftDown = true;
        return false;
    }

    public bool OnLeftUp(PointI point)
    {
        _last = point;
        _leftDown = false;

        switch (_mode)
        {
            case Mode.Anchoring:
            case Mode.Selecting:
                FinishGrid();
                break;

            case Mode.SnapMove:
                FinishSnapMove();
                break;
        }

        return false;
    }

    /// <summary>A mouse button other than the left one went down.</summary>
    public bool OnButtonDown(TriggerButton button, PointI point)
    {
        _last = point;
        int bit = 1 << (int)button;

        if (InGridMode && button != _gridButton)
        {
            // While the grid is showing the mouse belongs to Mullion: a stray click
            // must not reach whatever lies under the overlay.
            _hiddenButtons |= bit;
            return true;
        }

        switch (_mode)
        {
            case Mode.Anchoring:
                _hiddenButtons |= bit;
                return true;

            case Mode.Selecting:
                // A second click lets the user pick a new starting corner.
                _mode = Mode.Anchoring;
                _hiddenButtons |= bit;
                RefreshGrid();
                return true;

            default:
                if (button != _config.GridButton || !TryBeginGrid())
                {
                    return false;
                }

                _gridButton = button;
                _hiddenButtons |= bit;
                return true;
        }
    }

    /// <summary>A mouse button other than the left one was released.</summary>
    public bool OnButtonUp(TriggerButton button, PointI point)
    {
        _last = point;
        int bit = 1 << (int)button;

        if ((_hiddenButtons & bit) == 0)
        {
            return false;
        }

        // The matching press was hidden, so the release must be hidden too.
        _hiddenButtons &= ~bit;

        if (_mode == Mode.Anchoring && button == _gridButton)
        {
            var area = _host.WorkAreaAt(point);
            if (!area.IsEmpty)
            {
                _area = area;
                _anchor = area.Clamp(point);
                _mode = Mode.Selecting;
                RefreshGrid();
            }
        }

        return true;
    }

    public bool OnMove(PointI point)
    {
        _last = point;

        if (InGridMode)
        {
            RefreshGrid();
        }
        else if (_mode == Mode.SnapMove || (_leftDown && _moving != 0))
        {
            UpdateSnapMove();
        }

        return false;
    }

    public bool OnWheel(PointI point, int delta)
    {
        if (!InGridMode || delta == 0)
        {
            return false;
        }

        _last = point;

        // One notch of an ordinary wheel is one step. A smooth wheel reports a
        // notch in several small parts, which are added up until they make one.
        _wheel += delta;
        int steps = _wheel / WheelNotch;
        _wheel -= steps * WheelNotch;
        if (steps != 0)
        {
            var config = _config;
            _density = GridDensity.ClampLevel(config.Columns, config.Rows, _density + steps);
            RefreshGrid();
        }

        return true;
    }

    /// <summary>The system started moving or resizing a window.</summary>
    public void OnMoveSizeStart(nint window)
    {
        if (!InGridMode)
        {
            // A window Mullion is to leave alone is treated as if nothing were being
            // dragged: the grid button and the snap key keep their usual meaning.
            _moving = _host.CanArrange(window) ? window : 0;
        }
    }

    /// <summary>The system finished moving or resizing a window.</summary>
    public void OnMoveSizeEnd(nint window)
    {
        if (window == _moving)
        {
            _moving = 0;
            if (_mode == Mode.SnapMove)
            {
                _mode = Mode.Idle;
                Hide();
            }
        }

        if (InGridMode && window == _target && !_systemMoveEnded)
        {
            _systemMoveEnded = true;
            _host.SetTimer(false);
            _host.SetCancelKeyEnabled(true);
        }

        if (_pending is { } pending && pending.Window == window)
        {
            _pending = null;
            _host.SetTimer(false);
            _host.Place(pending.Window, pending.Bounds, pending.KeepSize, pending.Grid);
        }
    }

    /// <summary>The user pressed the key that abandons the gesture.</summary>
    public void OnCancelKey()
    {
        if (!InGridMode)
        {
            return;
        }

        _mode = Mode.Idle;
        _target = 0;
        Hide();
        _host.SetCancelKeyEnabled(false);
        _host.SetTimer(false);
    }

    /// <summary>The fallback timer fired: stop waiting for the system to report the end of its move.</summary>
    public void OnTimer()
    {
        _host.SetTimer(false);

        if (_pending is { } pending)
        {
            _pending = null;
            _host.Place(pending.Window, pending.Bounds, pending.KeepSize, pending.Grid);
        }
        else if (InGridMode && !_systemMoveEnded)
        {
            // No report arrived; enable the cancel key anyway. The placement itself
            // still waits for the report so it cannot race the system's own move.
            _host.SetCancelKeyEnabled(true);
        }
    }

    private bool TryBeginGrid()
    {
        var config = _config;
        if (config.Paused || !_leftDown || _moving == 0)
        {
            return false;
        }

        if (_host.WorkAreaAt(_last).IsEmpty)
        {
            return false;
        }

        _target = _moving;
        _mode = Mode.Anchoring;
        _systemMoveEnded = false;
        _density = 0;
        _wheel = 0;
        _pending = null;

        // Hand the window back to where it started. From here on only the overlay
        // moves, and the system's edge snapping can no longer interfere.
        _host.CancelSystemMove();
        _host.SetTimer(true);

        RefreshGrid();
        return true;
    }

    private void FinishGrid()
    {
        var config = _config;
        bool anchorFixed = _mode == Mode.Selecting;
        RectI area = anchorFixed ? _area : _host.WorkAreaAt(_last);
        nint target = _target;
        bool moveEnded = _systemMoveEnded;

        _mode = Mode.Idle;
        _target = 0;
        Hide();
        _host.SetCancelKeyEnabled(false);

        if (area.IsEmpty || target == 0)
        {
            _host.SetTimer(false);
            return;
        }

        var layout = CreateLayout(area, config, _density);
        var end = layout.CellAt(_last);
        var span = anchorFixed ? CellSpan.FromCorners(layout.CellAt(_anchor), end) : CellSpan.Single(end);
        var bounds = layout.WindowBounds(span);
        var grid = new GridPlacement(area, layout.Columns, layout.Rows, span);

        if (moveEnded)
        {
            _host.SetTimer(false);
            _host.Place(target, bounds, keepSize: false, grid);
        }
        else
        {
            // Released before the system finished handing the window back; wait for it.
            _pending = (target, bounds, false, grid);
            _host.SetTimer(true);
        }
    }

    private void RefreshGrid()
    {
        var config = _config;
        var (columns, rows) = GridDensity.Apply(config.Columns, config.Rows, _density);

        if (_mode == Mode.Anchoring)
        {
            var area = _host.WorkAreaAt(_last);
            if (area.IsEmpty)
            {
                return;
            }

            var layout = new GridLayout(area, columns, rows, config.WindowGap, config.EdgeGap);
            var span = CellSpan.Single(layout.CellAt(_last));
            Show(new OverlayModel(area, layout.Columns, layout.Rows, layout.WindowBounds(span), span, OverlayKind.Anchor));
        }
        else if (_mode == Mode.Selecting)
        {
            var layout = new GridLayout(_area, columns, rows, config.WindowGap, config.EdgeGap);
            var span = CellSpan.FromCorners(layout.CellAt(_anchor), layout.CellAt(_last));
            Show(new OverlayModel(_area, layout.Columns, layout.Rows, layout.WindowBounds(span), span, OverlayKind.Select));
        }
    }

    private void UpdateSnapMove()
    {
        var snapped = ComputeSnapMove(out var model);
        if (snapped is null || model is null)
        {
            if (_mode == Mode.SnapMove)
            {
                _mode = Mode.Idle;
                Hide();
            }

            return;
        }

        _mode = Mode.SnapMove;
        Show(model);
    }

    private void FinishSnapMove()
    {
        var snapped = ComputeSnapMove(out _);
        nint window = _moving;

        _mode = Mode.Idle;
        Hide();

        if (snapped is { } bounds && window != 0)
        {
            // The system is about to drop the window where the cursor is; move it
            // onto the grid once that has happened.
            _pending = (window, bounds, true, null);
            _host.SetTimer(true);
        }
    }

    private RectI? ComputeSnapMove(out OverlayModel? model)
    {
        model = null;
        var config = _config;

        if (config.Paused || !config.SnapMoveEnabled || _moving == 0 || !_host.IsSnapModifierDown())
        {
            return null;
        }

        var frame = _host.GetFrameBounds(_moving);
        var area = _host.WorkAreaAt(_last);
        if (frame is not { IsEmpty: false } window || area.IsEmpty)
        {
            return null;
        }

        var layout = CreateLayout(area, config, 0);
        var origin = layout.SnapOrigin(new PointI(window.Left, window.Top));
        var bounds = RectI.FromSize(origin.X, origin.Y, window.Width, window.Height);
        model = new OverlayModel(area, layout.Columns, layout.Rows, bounds, null, OverlayKind.SnapMove);
        return bounds;
    }

    private static GridLayout CreateLayout(RectI area, GestureConfig config, int density)
    {
        var (columns, rows) = GridDensity.Apply(config.Columns, config.Rows, density);
        return new GridLayout(area, columns, rows, config.WindowGap, config.EdgeGap);
    }

    private void Show(OverlayModel model)
    {
        if (model == _shown)
        {
            return;
        }

        _shown = model;
        _host.ShowOverlay(model);
    }

    private void Hide()
    {
        if (_shown is null)
        {
            return;
        }

        _shown = null;
        _host.HideOverlay();
    }
}
