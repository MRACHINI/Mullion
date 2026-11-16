namespace Mullion.Core;

/// <summary>
/// Temporary coarser or finer versions of the configured grid, chosen with the
/// mouse wheel while the grid is showing.
/// </summary>
public static class GridDensity
{
    /// <summary>Half as many cells in each direction.</summary>
    public const int MinLevel = -1;

    /// <summary>Four times as many cells in each direction.</summary>
    public const int MaxLevel = 3;

    public static int ClampLevel(int level) => Math.Clamp(level, MinLevel, MaxLevel);

    /// <summary>Level 0 is the configured grid; -1 halves it; 1, 2 and 3 multiply it by 2, 3 and 4.</summary>
    public static (int Columns, int Rows) Apply(int columns, int rows, int level)
    {
        level = ClampLevel(level);
        double factor = level < 0 ? 0.5 : level + 1;
        return (Scale(columns, factor), Scale(rows, factor));
    }

    private static int Scale(int count, double factor) =>
        Math.Max(1, (int)Math.Round(Math.Max(1, count) * factor, MidpointRounding.AwayFromZero));
}
