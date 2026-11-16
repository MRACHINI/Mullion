namespace Mullion.Core;

/// <summary>Works out which zone a point on the screen refers to.</summary>
public static class ZoneHitTest
{
    /// <summary>
    /// The position in <paramref name="zones"/> of the zone under a point, or -1.
    ///
    /// Zones may overlap (a "left third" inside a "left half"). Among the zones
    /// under the point, the one whose centre is nearest to the point wins, so
    /// moving towards the middle of a zone always selects it. When two centres are
    /// equally near, as with one zone centred inside another, the smaller zone wins.
    /// </summary>
    public static int Pick(IReadOnlyList<RectI> zones, PointI point)
    {
        int best = -1;
        long bestDistance = long.MaxValue;
        long bestArea = long.MaxValue;

        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i];
            if (!zone.Contains(point))
            {
                continue;
            }

            // Twice the offset from the centre, which avoids halves and compares the same.
            long dx = (2L * point.X) - zone.Left - zone.Right;
            long dy = (2L * point.Y) - zone.Top - zone.Bottom;
            long distance = (dx * dx) + (dy * dy);
            long area = (long)zone.Width * zone.Height;

            if (distance < bestDistance || (distance == bestDistance && area < bestArea))
            {
                best = i;
                bestDistance = distance;
                bestArea = area;
            }
        }

        return best;
    }
}
