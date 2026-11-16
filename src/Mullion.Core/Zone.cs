using System.Text.Json.Serialization;

namespace Mullion.Core;

/// <summary>
/// A saved part of the screen: a block of cells on a grid of its own, with an
/// optional keyboard shortcut that sends the active window there.
///
/// Each zone carries its own grid so that changing the main grid later does not
/// move existing zones.
/// </summary>
public sealed class Zone
{
    public const int MaxNameLength = 40;

    /// <summary>
    /// A short identifier that stays the same when the zone is renamed or moved in
    /// the list, so placement rules can refer to it. Filled in automatically.
    /// </summary>
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    /// <summary>Columns of the grid this zone is defined on.</summary>
    public int Columns { get; set; } = 2;

    /// <summary>Rows of the grid this zone is defined on.</summary>
    public int Rows { get; set; } = 1;

    public int Column0 { get; set; }
    public int Row0 { get; set; }
    public int Column1 { get; set; }
    public int Row1 { get; set; }

    /// <summary>The shortcut as text, such as "Ctrl+Alt+1"; empty for none.</summary>
    public string Shortcut { get; set; } = string.Empty;

    [JsonIgnore]
    public CellSpan Span
    {
        get => new(Column0, Row0, Column1, Row1);
        set
        {
            Column0 = value.Column0;
            Row0 = value.Row0;
            Column1 = value.Column1;
            Row1 = value.Row1;
        }
    }

    /// <summary>The shortcut, or null when there is none or it cannot be used.</summary>
    [JsonIgnore]
    public Hotkey? Hotkey =>
        Core.Hotkey.TryParse(Shortcut, out var hotkey) && hotkey.IsValid ? hotkey : null;

    /// <summary>Forces every value into its valid range.</summary>
    public void Normalize()
    {
        Id = (Id ?? string.Empty).Trim();
        if (Id.Length == 0)
        {
            Id = NewId();
        }

        Name = (Name ?? string.Empty).Trim();
        if (Name.Length > MaxNameLength)
        {
            Name = Name[..MaxNameLength];
        }

        // Wider than the settings window offers, because an area remembered from a
        // placement made at a finer density can be on a finer grid than that.
        Columns = Math.Clamp(Columns, 1, Settings.MaxAreaGridSize);
        Rows = Math.Clamp(Rows, 1, Settings.MaxAreaGridSize);

        int c0 = Math.Clamp(Math.Min(Column0, Column1), 0, Columns - 1);
        int c1 = Math.Clamp(Math.Max(Column0, Column1), 0, Columns - 1);
        int r0 = Math.Clamp(Math.Min(Row0, Row1), 0, Rows - 1);
        int r1 = Math.Clamp(Math.Max(Row0, Row1), 0, Rows - 1);
        Span = new CellSpan(c0, r0, c1, r1);

        Shortcut = Hotkey is { } hotkey ? hotkey.ToString() : string.Empty;
    }

    public Zone Clone() => (Zone)MemberwiseClone();

    /// <summary>A fresh identifier: eight random hexadecimal characters.</summary>
    public static string NewId() => Guid.NewGuid().ToString("N")[..8];

    /// <summary>Where a window in this zone sits on a monitor's work area.</summary>
    public RectI BoundsOn(RectI area, int windowGap, int edgeGap) =>
        new GridLayout(area, Columns, Rows, windowGap, edgeGap).WindowBounds(Span);
}
