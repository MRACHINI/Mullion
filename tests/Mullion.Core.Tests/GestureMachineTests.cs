using Mullion.Core;

namespace Mullion.Core.Tests;

public class GestureMachineTests
{
    private const nint Window = 0x1234;
    private static readonly RectI Area = new(0, 0, 1920, 1040);

    private sealed class FakeHost : IGestureHost
    {
        public RectI Area = GestureMachineTests.Area;
        public RectI? Frame = new RectI(500, 300, 1100, 700);
        public bool ModifierDown;
        public int SystemMoveCancels;
        public bool CancelKeyEnabled;
        public bool TimerRunning;
        public OverlayModel? Overlay;
        public int OverlayUpdates;
        public List<(nint Window, RectI Bounds, bool KeepSize)> Placements = [];

        public HashSet<nint> LeftAlone = [];

        public RectI WorkAreaAt(PointI point) => Area;
        public RectI? GetFrameBounds(nint window) => Frame;
        public bool CanArrange(nint window) => !LeftAlone.Contains(window);
        public bool IsSnapModifierDown() => ModifierDown;
        public void CancelSystemMove() => SystemMoveCancels++;
        public void SetCancelKeyEnabled(bool enabled) => CancelKeyEnabled = enabled;

        public void ShowOverlay(OverlayModel model)
        {
            Overlay = model;
            OverlayUpdates++;
        }

        public void HideOverlay() => Overlay = null;
        public GridPlacement? LastGrid;

        public void Place(nint window, RectI bounds, bool keepSize, GridPlacement? grid)
        {
            Placements.Add((window, bounds, keepSize));
            LastGrid = grid;
        }
        public void SetTimer(bool enabled) => TimerRunning = enabled;
    }

    private static (GestureMachine Machine, FakeHost Host) Create(GestureConfig? config = null)
    {
        var host = new FakeHost();
        var machine = new GestureMachine(host) { Config = config ?? new GestureConfig() };
        return (machine, host);
    }

    /// <summary>Presses the left button on a title bar and lets the system start its move.</summary>
    private static void StartDrag(GestureMachine machine)
    {
        Assert.False(machine.OnLeftDown(new PointI(600, 310)));
        machine.OnMoveSizeStart(Window);
        Assert.False(machine.OnMove(new PointI(620, 330)));
    }

    [Fact]
    public void Right_click_without_a_drag_is_left_alone()
    {
        var (machine, host) = Create();

        Assert.False(machine.OnRightDown(new PointI(10, 10)));
        Assert.False(machine.OnRightUp(new PointI(10, 10)));
        Assert.Null(host.Overlay);
        Assert.Equal(0, host.SystemMoveCancels);
    }

    [Fact]
    public void Right_click_after_the_drag_has_ended_is_left_alone()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnLeftUp(new PointI(620, 330));
        machine.OnMoveSizeEnd(Window);

        Assert.False(machine.OnRightDown(new PointI(620, 330)));
        Assert.Null(host.Overlay);
    }

    [Fact]
    public void Full_gesture_places_the_window_on_the_selected_cells()
    {
        var (machine, host) = Create();
        StartDrag(machine);

        // Right button down: grid appears, the system move is cancelled, the click is hidden.
        Assert.True(machine.OnRightDown(new PointI(500, 300)));
        Assert.Equal(1, host.SystemMoveCancels);
        Assert.NotNull(host.Overlay);
        Assert.Equal(OverlayKind.Anchor, host.Overlay!.Kind);
        Assert.Equal(new CellSpan(1, 1, 1, 1), host.Overlay.Span);

        machine.OnMoveSizeEnd(Window);
        Assert.True(host.CancelKeyEnabled);

        // Right button up fixes the corner in cell (1,1).
        Assert.True(machine.OnRightUp(new PointI(500, 300)));
        Assert.Equal(OverlayKind.Select, host.Overlay!.Kind);

        // Moving stretches the selection to cell (2,3).
        Assert.False(machine.OnMove(new PointI(1300, 1000)));
        Assert.Equal(new CellSpan(1, 1, 2, 3), host.Overlay!.Span);
        Assert.Equal(new RectI(480, 260, 1440, 1040), host.Overlay.Highlight);

        // Left button up places the window and is not hidden.
        Assert.False(machine.OnLeftUp(new PointI(1300, 1000)));
        Assert.Null(host.Overlay);
        Assert.False(host.CancelKeyEnabled);
        Assert.Equal([(Window, new RectI(480, 260, 1440, 1040), false)], host.Placements);
    }

    [Fact]
    public void A_window_to_be_left_alone_gets_no_grid()
    {
        var (machine, host) = Create();
        host.LeftAlone.Add(Window);
        StartDrag(machine);

        // The right-click is not hidden and nothing is shown or cancelled.
        Assert.False(machine.OnRightDown(new PointI(500, 300)));
        Assert.False(machine.OnRightUp(new PointI(500, 300)));
        Assert.Null(host.Overlay);
        Assert.Equal(0, host.SystemMoveCancels);

        machine.OnLeftUp(new PointI(500, 300));
        machine.OnMoveSizeEnd(Window);
        Assert.Empty(host.Placements);
    }

    [Fact]
    public void A_window_to_be_left_alone_is_not_snapped_either()
    {
        var (machine, host) = Create();
        host.LeftAlone.Add(Window);
        host.ModifierDown = true;
        StartDrag(machine);

        Assert.Null(host.Overlay);

        machine.OnLeftUp(new PointI(620, 330));
        machine.OnMoveSizeEnd(Window);
        Assert.Empty(host.Placements);
    }

    [Fact]
    public void Leaving_one_window_alone_does_not_affect_the_next_drag()
    {
        var (machine, host) = Create();
        host.LeftAlone.Add(0x9999);

        Assert.False(machine.OnLeftDown(new PointI(600, 310)));
        machine.OnMoveSizeStart(0x9999);
        machine.OnLeftUp(new PointI(600, 310));
        machine.OnMoveSizeEnd(0x9999);

        StartDrag(machine);
        Assert.True(machine.OnRightDown(new PointI(500, 300)));
        Assert.NotNull(host.Overlay);
    }

    [Fact]
    public void A_grid_placement_reports_the_cells_that_were_selected()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(500, 300));
        machine.OnMoveSizeEnd(Window);
        machine.OnRightUp(new PointI(500, 300));
        machine.OnWheel(new PointI(500, 300), 120);
        machine.OnMove(new PointI(1300, 1000));
        machine.OnLeftUp(new PointI(1300, 1000));

        // The wheel doubled the 4 x 4 grid, so the cells are counted on 8 x 8.
        Assert.Equal(new GridPlacement(Area, 8, 8, new CellSpan(2, 2, 5, 7)), host.LastGrid);
    }

    [Fact]
    public void A_snap_move_reports_no_cells()
    {
        var (machine, host) = Create();
        host.ModifierDown = true;
        StartDrag(machine);
        machine.OnLeftUp(new PointI(620, 330));
        machine.OnMoveSizeEnd(Window);

        Assert.Single(host.Placements);
        Assert.Null(host.LastGrid);
    }

    [Fact]
    public void Selection_can_grow_up_and_to_the_left_of_the_corner()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(1500, 900));
        machine.OnMoveSizeEnd(Window);
        machine.OnRightUp(new PointI(1500, 900));

        machine.OnMove(new PointI(100, 100));
        machine.OnLeftUp(new PointI(100, 100));

        Assert.Equal([(Window, Area, false)], host.Placements);
    }

    [Fact]
    public void Corner_follows_the_cursor_while_the_right_button_is_held()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        machine.OnMoveSizeEnd(Window);

        machine.OnMove(new PointI(1500, 900));
        Assert.Equal(new CellSpan(3, 3, 3, 3), host.Overlay!.Span);

        machine.OnRightUp(new PointI(1500, 900));
        machine.OnLeftUp(new PointI(1500, 900));

        Assert.Equal([(Window, new RectI(1440, 780, 1920, 1040), false)], host.Placements);
    }

    [Fact]
    public void Releasing_left_while_right_is_still_held_places_on_one_cell()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        machine.OnMoveSizeEnd(Window);

        machine.OnLeftUp(new PointI(100, 100));

        Assert.Equal([(Window, new RectI(0, 0, 480, 260), false)], host.Placements);

        // The right button release that follows must still be hidden, once.
        Assert.True(machine.OnRightUp(new PointI(100, 100)));
        Assert.False(machine.OnRightUp(new PointI(100, 100)));
    }

    [Fact]
    public void Placement_waits_until_the_system_has_given_the_window_back()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        machine.OnRightUp(new PointI(100, 100));

        machine.OnLeftUp(new PointI(100, 100));

        Assert.Empty(host.Placements);
        Assert.True(host.TimerRunning);

        machine.OnMoveSizeEnd(Window);

        Assert.Single(host.Placements);
        Assert.False(host.TimerRunning);
    }

    [Fact]
    public void Placement_happens_on_the_timer_when_the_system_never_reports_back()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        machine.OnRightUp(new PointI(100, 100));
        machine.OnLeftUp(new PointI(100, 100));

        machine.OnTimer();

        Assert.Single(host.Placements);
        Assert.False(host.TimerRunning);

        // A late report must not place the window a second time.
        machine.OnMoveSizeEnd(Window);
        Assert.Single(host.Placements);
    }

    [Fact]
    public void Timer_enables_the_cancel_key_when_the_system_never_reports_back()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        Assert.False(host.CancelKeyEnabled);

        machine.OnTimer();

        Assert.True(host.CancelKeyEnabled);
        Assert.NotNull(host.Overlay);
    }

    [Fact]
    public void Cancel_key_abandons_the_gesture_without_moving_the_window()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        machine.OnMoveSizeEnd(Window);

        machine.OnCancelKey();

        Assert.Null(host.Overlay);
        Assert.False(host.CancelKeyEnabled);

        Assert.True(machine.OnRightUp(new PointI(100, 100)));
        Assert.False(machine.OnLeftUp(new PointI(100, 100)));
        Assert.Empty(host.Placements);
    }

    [Fact]
    public void Second_right_click_picks_a_new_corner()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        machine.OnMoveSizeEnd(Window);
        machine.OnRightUp(new PointI(100, 100));
        machine.OnMove(new PointI(1000, 600));

        Assert.True(machine.OnRightDown(new PointI(1000, 600)));
        Assert.Equal(OverlayKind.Anchor, host.Overlay!.Kind);
        Assert.Equal(new CellSpan(2, 2, 2, 2), host.Overlay.Span);
        Assert.Equal(1, host.SystemMoveCancels);

        Assert.True(machine.OnRightUp(new PointI(1000, 600)));
        machine.OnMove(new PointI(1900, 1000));
        machine.OnLeftUp(new PointI(1900, 1000));

        Assert.Equal([(Window, new RectI(960, 520, 1920, 1040), false)], host.Placements);
    }

    [Fact]
    public void Wheel_changes_the_grid_density_only_while_the_grid_is_showing()
    {
        var (machine, host) = Create();

        Assert.False(machine.OnWheel(new PointI(100, 100), 120));

        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        machine.OnMoveSizeEnd(Window);
        machine.OnRightUp(new PointI(100, 100));

        Assert.True(machine.OnWheel(new PointI(100, 100), 120));
        Assert.Equal((8, 8), (host.Overlay!.Columns, host.Overlay.Rows));

        Assert.True(machine.OnWheel(new PointI(100, 100), -120));
        Assert.True(machine.OnWheel(new PointI(100, 100), -120));
        Assert.Equal((2, 2), (host.Overlay!.Columns, host.Overlay.Rows));

        // The density cannot go below half.
        Assert.True(machine.OnWheel(new PointI(100, 100), -120));
        Assert.Equal((2, 2), (host.Overlay!.Columns, host.Overlay.Rows));

        machine.OnLeftUp(new PointI(100, 100));
        Assert.Equal([(Window, new RectI(0, 0, 960, 520), false)], host.Placements);
    }

    [Fact]
    public void Density_starts_fresh_for_each_gesture()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        machine.OnMoveSizeEnd(Window);
        machine.OnWheel(new PointI(100, 100), 120);
        machine.OnLeftUp(new PointI(100, 100));
        machine.OnRightUp(new PointI(100, 100));

        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));

        Assert.Equal((4, 4), (host.Overlay!.Columns, host.Overlay.Rows));
    }

    [Fact]
    public void Paused_app_ignores_the_gesture()
    {
        var (machine, host) = Create(new GestureConfig(Paused: true));
        StartDrag(machine);

        Assert.False(machine.OnRightDown(new PointI(100, 100)));
        Assert.Null(host.Overlay);
        Assert.Equal(0, host.SystemMoveCancels);
    }

    [Fact]
    public void Gaps_from_the_settings_are_applied_to_the_placement()
    {
        var (machine, host) = Create(new GestureConfig(WindowGap: 10, EdgeGap: 20));
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        machine.OnMoveSizeEnd(Window);
        machine.OnRightUp(new PointI(100, 100));
        machine.OnLeftUp(new PointI(100, 100));

        Assert.Equal([(Window, new RectI(20, 20, 475, 255), false)], host.Placements);
    }

    [Fact]
    public void Overlay_is_not_redrawn_when_nothing_changed()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        int updates = host.OverlayUpdates;

        machine.OnMove(new PointI(101, 101));
        machine.OnMove(new PointI(102, 102));

        Assert.Equal(updates, host.OverlayUpdates);
    }

    [Fact]
    public void Moves_started_while_the_grid_is_showing_do_not_change_the_target()
    {
        var (machine, host) = Create();
        StartDrag(machine);
        machine.OnRightDown(new PointI(100, 100));
        machine.OnMoveSizeEnd(Window);

        machine.OnMoveSizeStart(0x9999);
        machine.OnLeftUp(new PointI(100, 100));

        Assert.Equal(Window, Assert.Single(host.Placements).Window);
    }

    [Fact]
    public void Holding_the_modifier_while_dragging_snaps_the_window_without_resizing()
    {
        var (machine, host) = Create();
        host.ModifierDown = true;
        StartDrag(machine);

        // The window is 600 x 400 at (500, 300); the nearest grid origin is (480, 260).
        Assert.Equal(OverlayKind.SnapMove, host.Overlay!.Kind);
        Assert.Equal(new RectI(480, 260, 1080, 660), host.Overlay.Highlight);
        Assert.Equal(0, host.SystemMoveCancels);

        Assert.False(machine.OnLeftUp(new PointI(620, 330)));
        Assert.Null(host.Overlay);
        Assert.Empty(host.Placements);

        machine.OnMoveSizeEnd(Window);

        Assert.Equal([(Window, new RectI(480, 260, 1080, 660), true)], host.Placements);
    }

    [Fact]
    public void Releasing_the_modifier_before_the_button_leaves_the_window_where_it_was_dropped()
    {
        var (machine, host) = Create();
        host.ModifierDown = true;
        StartDrag(machine);

        host.ModifierDown = false;
        machine.OnMove(new PointI(640, 350));
        Assert.Null(host.Overlay);

        machine.OnLeftUp(new PointI(640, 350));
        machine.OnMoveSizeEnd(Window);

        Assert.Empty(host.Placements);
    }

    [Fact]
    public void Snap_move_can_be_switched_off()
    {
        var (machine, host) = Create(new GestureConfig(SnapMoveEnabled: false));
        host.ModifierDown = true;
        StartDrag(machine);

        machine.OnLeftUp(new PointI(620, 330));
        machine.OnMoveSizeEnd(Window);

        Assert.Null(host.Overlay);
        Assert.Empty(host.Placements);
    }

    [Fact]
    public void Right_click_during_a_snap_move_switches_to_the_grid()
    {
        var (machine, host) = Create();
        host.ModifierDown = true;
        StartDrag(machine);

        Assert.True(machine.OnRightDown(new PointI(100, 100)));

        Assert.Equal(OverlayKind.Anchor, host.Overlay!.Kind);
        Assert.Equal(1, host.SystemMoveCancels);
    }
}
