using Mullion.Core;
using Mullion.Interop;
using Mullion.UI;

namespace Mullion;

/// <summary>
/// "Arrange windows": goes through the open windows one at a time, shows the saved
/// zones on every monitor, and puts each window in the zone the user clicks.
/// Runs on the user interface thread.
/// </summary>
internal sealed class ArrangeSession
{
    private readonly Func<Settings> _settings;
    private readonly List<ArrangeOverlay> _overlays = [];
    private List<WindowList.Entry> _windows = [];
    private List<RectI> _monitors = [];
    private List<Zone> _zones = [];
    private int _windowGap;
    private int _edgeGap;
    private int _index;
    private bool _closing;
    private volatile bool _active;

    // Every window is brought forward while it is the current one, which shuffles
    // the stacking order. These let the original order be put back at the end.
    private List<nint> _originalOrder = [];
    private readonly HashSet<nint> _broughtForward = [];

    public ArrangeSession(Func<Settings> settings)
    {
        _settings = settings;
    }

    /// <summary>True while windows are being arranged. Safe to read from any thread.</summary>
    public bool IsActive => _active;

    /// <summary>
    /// Starts arranging. Returns null on success, or a sentence explaining why
    /// there is nothing to do.
    /// </summary>
    public string? Start()
    {
        if (IsActive)
        {
            _overlays[0].Activate();
            return null;
        }

        var settings = _settings();
        if (settings.Zones.Count == 0)
        {
            return "There are no zones yet.\n\nZones are what you click to place each window. Add some under Settings → Zones first.";
        }

        var windows = WindowList.GetArrangeable();
        if (windows.Count == 0)
        {
            return "There are no open windows to arrange.";
        }

        var monitors = Native.GetWorkAreas();
        if (monitors.Count == 0)
        {
            return "Mullion could not find a monitor to show the zones on.";
        }

        _windows = windows;
        _originalOrder = windows.Select(entry => entry.Handle).ToList();
        _broughtForward.Clear();
        _monitors = monitors;
        _zones = settings.Zones.Select(zone => zone.Clone()).ToList();
        _windowGap = settings.WindowGap;
        _edgeGap = settings.EdgeGap;
        _index = 0;
        _closing = false;

        for (int m = 0; m < monitors.Count; m++)
        {
            int monitor = m;
            var area = monitors[m];
            var zones = _zones
                .Select(zone => new ArrangeZone(zone.BoundsOn(area, _windowGap, _edgeGap), zone.Name, zone.Shortcut))
                .ToList();

            var overlay = new ArrangeOverlay(area, zones, settings.Appearance);
            overlay.ZoneChosen += zone => Place(zone, monitor);
            overlay.SkipRequested += Skip;
            overlay.BackRequested += Back;
            overlay.DoneRequested += Finish;
            overlay.Closed += (_, _) => Finish();
            _overlays.Add(overlay);
            overlay.ShowOverMonitor();
        }

        _active = true;
        ShowCurrent();

        // Give the keyboard to the overlay under the mouse, so Esc and Space work.
        if (IsActive)
        {
            int under = 0;
            if (Native.GetCursorPos(out var cursor))
            {
                var area = Native.WorkAreaAt(new PointI(cursor.X, cursor.Y));
                under = Math.Max(0, _monitors.IndexOf(area));
            }

            _overlays[Math.Min(under, _overlays.Count - 1)].Activate();
        }

        return null;
    }

    /// <summary>
    /// Puts the current window in a zone chosen by its keyboard shortcut, on the
    /// monitor the window is already on.
    /// </summary>
    public void PlaceByShortcut(int zone)
    {
        if (!IsActive || _index >= _windows.Count)
        {
            return;
        }

        var frame = Native.GetFrameBounds(_windows[_index].Handle);
        Place(zone, frame is { } bounds ? MonitorList.IndexOf(_monitors, bounds) : 0);
    }

    /// <summary>Ends arranging and removes the overlays.</summary>
    public void Finish()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        _active = false;
        foreach (var overlay in _overlays.ToList())
        {
            overlay.Close();
        }

        _overlays.Clear();
        _windows = [];
        RestoreStackingOrder();
    }

    /// <summary>
    /// Puts the windows that were brought forward back in the front-to-back order
    /// they had before arranging started, so that only their positions have changed.
    /// </summary>
    private void RestoreStackingOrder()
    {
        // Raising them from the back-most to the front-most leaves the front-most on top.
        for (int i = _originalOrder.Count - 1; i >= 0; i--)
        {
            nint window = _originalOrder[i];
            if (_broughtForward.Contains(window) && Native.IsWindow(window))
            {
                WindowPlacer.BringToFront(window);
            }
        }

        _originalOrder = [];
        _broughtForward.Clear();
    }

    private void Place(int zone, int monitor)
    {
        if (!IsActive || _index >= _windows.Count || zone < 0 || zone >= _zones.Count)
        {
            return;
        }

        nint window = _windows[_index].Handle;
        var bounds = _zones[zone].BoundsOn(_monitors[Math.Clamp(monitor, 0, _monitors.Count - 1)], _windowGap, _edgeGap);
        Log.Trace($"arrange: window {_index + 1} ({Native.GetClassName(window)}) to zone {zone} on monitor {monitor}: {bounds}");

        // Moving a window waits for its application; keep the overlay responsive.
        Task.Run(() =>
        {
            try
            {
                WindowPlacer.Place(window, bounds, keepSize: false);
            }
            catch (Exception ex)
            {
                Log.Write("Placing a window while arranging failed.", ex);
            }
        });

        _index++;
        ShowCurrent();
    }

    private void Skip()
    {
        if (IsActive)
        {
            _index++;
            ShowCurrent();
        }
    }

    private void Back()
    {
        if (IsActive && _index > 0)
        {
            _index--;
            ShowCurrent(goingBack: true);
        }
    }

    /// <summary>Shows the window at the current position, passing over any that have closed meanwhile.</summary>
    private void ShowCurrent(bool goingBack = false)
    {
        while (_index >= 0 && _index < _windows.Count && !WindowList.IsStillArrangeable(_windows[_index].Handle))
        {
            _windows.RemoveAt(_index);
            if (goingBack && _index > 0)
            {
                _index--;
            }
        }

        if (_index >= _windows.Count)
        {
            Finish();
            return;
        }

        var entry = _windows[_index];
        if (Log.TraceEnabled)
        {
            Log.Trace($"arrange: showing window {_index + 1} of {_windows.Count} ({WindowList.ProgramOf(entry.Handle)}, {Native.GetClassName(entry.Handle)})");
        }

        // Bring it in front of the other windows, so what is being placed can be
        // seen. The keyboard stays with the overlay, for Esc and Space.
        WindowPlacer.BringToFront(entry.Handle);
        _broughtForward.Add(entry.Handle);

        var frame = Native.GetFrameBounds(entry.Handle);
        string title = Native.GetWindowTitle(entry.Handle);
        foreach (var overlay in _overlays)
        {
            overlay.KeepOnTop();
            overlay.SetCurrent(frame, title.Length > 0 ? title : entry.Title, _index + 1, _windows.Count);
        }
    }
}
