namespace Mullion.Core;

/// <summary>
/// Divides a screen area into equal columns and rows and answers where cells
/// are and where a window placed on them should go.
/// </summary>
public sealed class GridLayout
{
    public GridLayout(RectI area, int columns, int rows, int windowGap = 0, int edgeGap = 0)
    {
        if (area.IsEmpty)
        {
            throw new ArgumentException("The grid area must not be empty.", nameof(area));
        }

        Area = area;
        Columns = Math.Clamp(columns, 1, area.Width);
        Rows = Math.Clamp(rows, 1, area.Height);
        WindowGap = Math.Max(0, windowGap);
        EdgeGap = Math.Max(0, edgeGap);
    }

    public RectI Area { get; }
    public int Columns { get; }
    public int Rows { get; }

    /// <summary>Space left between two neighbouring windows, in pixels.</summary>
    public int WindowGap { get; }

    /// <summary>Space left between a window and the edge of the area, in pixels.</summary>
    public int EdgeGap { get; }

    /// <summary>X of the line before column <paramref name="index"/>; index == Columns is the right edge.</summary>
    public int ColumnEdge(int index) => Area.Left + Offset(index, Columns, Area.Width);

    /// <summary>Y of the line above row <paramref name="index"/>; index == Rows is the bottom edge.</summary>
    public int RowEdge(int index) => Area.Top + Offset(index, Rows, Area.Height);

    /// <summary>The cell under a point. Points outside the area map to the nearest cell.</summary>
    public Cell CellAt(PointI point)
    {
        var p = Area.Clamp(point);
        return new Cell(
            Locate(p.X - Area.Left, Columns, Area.Width),
            Locate(p.Y - Area.Top, Rows, Area.Height));
    }

    /// <summary>The full rectangle covered by a block of cells, ignoring gaps.</summary>
    public RectI CellBounds(CellSpan span)
    {
        span = ClampSpan(span);
        return new RectI(
            ColumnEdge(span.Column0), RowEdge(span.Row0),
            ColumnEdge(span.Column1 + 1), RowEdge(span.Row1 + 1));
    }

    /// <summary>Where a window occupying a block of cells should sit once gaps are applied.</summary>
    public RectI WindowBounds(CellSpan span)
    {
        span = ClampSpan(span);
        var cells = CellBounds(span);

        // An inner edge gives up half the gap so two neighbours end up one full gap apart.
        int near = WindowGap / 2;
        int far = WindowGap - near;

        int left = cells.Left + (span.Column0 == 0 ? EdgeGap : near);
        int top = cells.Top + (span.Row0 == 0 ? EdgeGap : near);
        int right = cells.Right - (span.Column1 == Columns - 1 ? EdgeGap : far);
        int bottom = cells.Bottom - (span.Row1 == Rows - 1 ? EdgeGap : far);

        // Gaps larger than the cell would invert the rectangle; ignore them on that axis.
        if (right - left < 1)
        {
            left = cells.Left;
            right = cells.Right;
        }

        if (bottom - top < 1)
        {
            top = cells.Top;
            bottom = cells.Bottom;
        }

        return new RectI(left, top, right, bottom);
    }

    /// <summary>
    /// The window origin on the grid that is closest to <paramref name="topLeft"/>.
    /// Used to move a window onto the grid without changing its size.
    /// </summary>
    public PointI SnapOrigin(PointI topLeft)
    {
        int bestX = 0, bestY = 0;
        long bestDx = long.MaxValue, bestDy = long.MaxValue;

        for (int c = 0; c < Columns; c++)
        {
            int x = WindowBounds(new CellSpan(c, 0, c, 0)).Left;
            long d = Math.Abs((long)x - topLeft.X);
            if (d < bestDx)
            {
                bestDx = d;
                bestX = x;
            }
        }

        for (int r = 0; r < Rows; r++)
        {
            int y = WindowBounds(new CellSpan(0, r, 0, r)).Top;
            long d = Math.Abs((long)y - topLeft.Y);
            if (d < bestDy)
            {
                bestDy = d;
                bestY = y;
            }
        }

        return new PointI(bestX, bestY);
    }

    private CellSpan ClampSpan(CellSpan span)
    {
        int c0 = Math.Clamp(Math.Min(span.Column0, span.Column1), 0, Columns - 1);
        int c1 = Math.Clamp(Math.Max(span.Column0, span.Column1), 0, Columns - 1);
        int r0 = Math.Clamp(Math.Min(span.Row0, span.Row1), 0, Rows - 1);
        int r1 = Math.Clamp(Math.Max(span.Row0, span.Row1), 0, Rows - 1);
        return new CellSpan(c0, r0, c1, r1);
    }

    private static int Offset(int index, int count, int size) =>
        (int)((long)size * Math.Clamp(index, 0, count) / count);

    private static int Locate(int offset, int count, int size)
    {
        int i = Math.Clamp((int)((long)offset * count / size), 0, count - 1);

        // Integer rounding of the edges can put the first guess one cell off.
        while (i + 1 < count && Offset(i + 1, count, size) <= offset)
        {
            i++;
        }

        while (i > 0 && Offset(i, count, size) > offset)
        {
            i--;
        }

        return i;
    }
}
