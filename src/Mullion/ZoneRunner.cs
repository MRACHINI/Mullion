using Mullion.Core;
using Mullion.Interop;

namespace Mullion;

/// <summary>Carries out a zone shortcut: moves the active window into the zone.</summary>
internal sealed class ZoneRunner
{
    private sealed record Snapshot(IReadOnlyList<Zone> Zones, int WindowGap, int EdgeGap, bool Cycle);

    private readonly object _gate = new();
    private ZoneCycler _cycler = new();
    private volatile Snapshot _snapshot = new([], 0, 0, true);

    /// <summary>Takes a copy of the zones and margins. Safe to call from the user interface.</summary>
    public void Update(Settings settings)
    {
        var zones = settings.Zones.Select(zone => zone.Clone()).ToList();
        lock (_gate)
        {
            _snapshot = new Snapshot(zones, settings.WindowGap, settings.EdgeGap, settings.CycleMonitors);

            // Zone positions in the list may have changed, so forget the last press.
            _cycler = new ZoneCycler();
        }
    }

    /// <summary>Moves the active window into the zone with the given position in the list.</summary>
    public void Run(int zoneIndex)
    {
        nint window = Native.GetForegroundWindow();
        if (window == 0)
        {
            return;
        }

        nint root = Native.GetAncestor(window, Native.GA_ROOT);
        if (root != 0)
        {
            window = root;
        }

        if (!CanPlace(window))
        {
            return;
        }

        var monitors = Native.GetWorkAreas();
        if (monitors.Count == 0)
        {
            return;
        }

        var frame = Native.GetFrameBounds(window);
        int current = frame is { } bounds ? MonitorList.IndexOf(monitors, bounds) : 0;

        RectI target;
        int monitor;
        string zoneId;
        lock (_gate)
        {
            var snapshot = _snapshot;
            if (zoneIndex < 0 || zoneIndex >= snapshot.Zones.Count)
            {
                return;
            }

            zoneId = snapshot.Zones[zoneIndex].Id;
            (monitor, target) = _cycler.Plan(
                window,
                zoneIndex,
                snapshot.Zones[zoneIndex],
                frame,
                monitors,
                current,
                snapshot.WindowGap,
                snapshot.EdgeGap,
                snapshot.Cycle);
        }

        WindowPlacer.Place(window, target, keepSize: false);
        Placed?.Invoke(window, new PlacementRule { ZoneId = zoneId, Monitor = monitor + 1 });
    }

    /// <summary>
    /// Called after a window was placed in a zone, with the window and where it
    /// went: a rule with only its zone and monitor filled in.
    /// </summary>
    public Action<nint, PlacementRule>? Placed { get; set; }

    private static bool CanPlace(nint window) =>
        Native.IsWindow(window)
        && Native.IsWindowVisible(window)
        && !Native.IsIconic(window)
        && !WindowList.IsShellWindow(window);
}
