namespace Mullion.Core;

/// <summary>Helpers for working with the work areas of several monitors.</summary>
public static class MonitorList
{
    /// <summary>Orders monitors left to right, then top to bottom.</summary>
    public static List<RectI> Sort(IEnumerable<RectI> areas) =>
        areas.Where(a => !a.IsEmpty).OrderBy(a => a.Left).ThenBy(a => a.Top).ToList();

    /// <summary>
    /// The monitor a window is on: the one it overlaps most, or the nearest one
    /// when it overlaps none.
    /// </summary>
    public static int IndexOf(IReadOnlyList<RectI> areas, RectI window)
    {
        int best = 0;
        long bestOverlap = -1;
        long bestDistance = long.MaxValue;

        for (int i = 0; i < areas.Count; i++)
        {
            var area = areas[i];
            long w = Math.Min(area.Right, window.Right) - Math.Max(area.Left, window.Left);
            long h = Math.Min(area.Bottom, window.Bottom) - Math.Max(area.Top, window.Top);
            long overlap = w > 0 && h > 0 ? w * h : 0;

            long dx = ((long)area.Left + area.Right - window.Left - window.Right) / 2;
            long dy = ((long)area.Top + area.Bottom - window.Top - window.Bottom) / 2;
            long distance = (dx * dx) + (dy * dy);

            if (overlap > bestOverlap || (overlap == bestOverlap && distance < bestDistance))
            {
                best = i;
                bestOverlap = overlap;
                bestDistance = distance;
            }
        }

        return best;
    }
}

/// <summary>
/// Decides where a zone shortcut sends a window. The first press puts the window
/// in the zone on its current monitor; pressing again while it is already there
/// moves it to the same zone on the next monitor.
/// </summary>
public sealed class ZoneCycler
{
    private (nint Window, int Zone, int Monitor)? _last;

    /// <param name="window">The window to move.</param>
    /// <param name="zoneIndex">Identifies the zone between calls.</param>
    /// <param name="zone">The zone.</param>
    /// <param name="frame">The window's current visible bounds, if known.</param>
    /// <param name="monitors">Work areas of all monitors, in cycling order. Must not be empty.</param>
    /// <param name="currentMonitor">Index of the monitor the window is on.</param>
    /// <param name="windowGap">Gap between neighbouring windows.</param>
    /// <param name="edgeGap">Gap at the screen edges.</param>
    /// <param name="cycle">Whether a repeated press moves to the next monitor.</param>
    public (int Monitor, RectI Bounds) Plan(
        nint window,
        int zoneIndex,
        Zone zone,
        RectI? frame,
        IReadOnlyList<RectI> monitors,
        int currentMonitor,
        int windowGap,
        int edgeGap,
        bool cycle)
    {
        if (monitors.Count == 0)
        {
            throw new ArgumentException("At least one monitor is required.", nameof(monitors));
        }

        int monitor = Math.Clamp(currentMonitor, 0, monitors.Count - 1);
        var bounds = zone.BoundsOn(monitors[monitor], windowGap, edgeGap);

        if (cycle && monitors.Count > 1 && frame is { } now && IsInZone(now, bounds, window, zoneIndex, monitor))
        {
            monitor = (monitor + 1) % monitors.Count;
            bounds = zone.BoundsOn(monitors[monitor], windowGap, edgeGap);
        }

        _last = (window, zoneIndex, monitor);
        return (monitor, bounds);
    }

    private bool IsInZone(RectI frame, RectI bounds, nint window, int zoneIndex, int monitor)
    {
        if (!Near(frame.Left, bounds.Left) || !Near(frame.Top, bounds.Top))
        {
            return false;
        }

        if (Near(frame.Width, bounds.Width) && Near(frame.Height, bounds.Height))
        {
            return true;
        }

        // A window that refuses the zone's size still counts as "in the zone" when
        // this same shortcut put it at this position last time.
        return _last == (window, zoneIndex, monitor);
    }

    private static bool Near(int a, int b) => Math.Abs(a - b) <= 1;
}
