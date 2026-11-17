namespace Mullion.Core;

/// <summary>
/// Temporary coarser or finer versions of the configured grid, chosen with the
/// mouse wheel while the grid is showing. Each step adds or removes one column
/// and one row.
/// </summary>
public static class GridDensity
{
    /// <summary>
    /// The nearest step the grid can take: neither direction may drop below one
    /// cell or exceed <see cref="Settings.MaxGridSize"/>.
    /// </summary>
    public static int ClampLevel(int columns, int rows, int level)
    {
        columns = Math.Clamp(columns, 1, Settings.MaxGridSize);
        rows = Math.Clamp(rows, 1, Settings.MaxGridSize);
        int lowest = 1 - Math.Min(columns, rows);
        int highest = Settings.MaxGridSize - Math.Max(columns, rows);
        return Math.Clamp(level, lowest, highest);
    }

    /// <summary>Level 0 is the configured grid; each level adds one column and one row.</summary>
    public static (int Columns, int Rows) Apply(int columns, int rows, int level)
    {
        columns = Math.Clamp(columns, 1, Settings.MaxGridSize);
        rows = Math.Clamp(rows, 1, Settings.MaxGridSize);
        level = ClampLevel(columns, rows, level);
        return (columns + level, rows + level);
    }
}
