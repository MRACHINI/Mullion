namespace Mullion.Core;

/// <summary>A point in physical screen pixels.</summary>
public readonly record struct PointI(int X, int Y);

/// <summary>A rectangle in physical screen pixels. Right and Bottom are exclusive.</summary>
public readonly record struct RectI(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static RectI FromSize(int left, int top, int width, int height) =>
        new(left, top, left + width, top + height);

    public bool Contains(PointI p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    /// <summary>Returns the nearest point that lies inside the rectangle.</summary>
    public PointI Clamp(PointI p) => new(
        Math.Clamp(p.X, Left, Math.Max(Left, Right - 1)),
        Math.Clamp(p.Y, Top, Math.Max(Top, Bottom - 1)));
}

/// <summary>One grid cell, zero-based from the top-left.</summary>
public readonly record struct Cell(int Column, int Row);

/// <summary>An inclusive block of grid cells.</summary>
public readonly record struct CellSpan(int Column0, int Row0, int Column1, int Row1)
{
    public int Columns => Column1 - Column0 + 1;
    public int Rows => Row1 - Row0 + 1;

    public static CellSpan Single(Cell cell) => new(cell.Column, cell.Row, cell.Column, cell.Row);

    /// <summary>The block that has the two cells as opposite corners, in any order.</summary>
    public static CellSpan FromCorners(Cell a, Cell b) => new(
        Math.Min(a.Column, b.Column), Math.Min(a.Row, b.Row),
        Math.Max(a.Column, b.Column), Math.Max(a.Row, b.Row));
}
