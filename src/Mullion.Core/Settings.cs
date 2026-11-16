namespace Mullion.Core;

/// <summary>The key that turns an ordinary window drag into a move on the grid.</summary>
public enum SnapModifier
{
    None,
    Ctrl,
    Shift,
    Alt,
}

/// <summary>Colours and opacities of the grid overlay. Colours are #RRGGBB.</summary>
public sealed class AppearanceSettings
{
    public string TintColor { get; set; } = "#000000";
    public double TintOpacity { get; set; } = 0.30;
    public string LineColor { get; set; } = "#FFFFFF";
    public double LineOpacity { get; set; } = 0.45;
    public string SelectionColor { get; set; } = "#3B82F6";
    public double SelectionOpacity { get; set; } = 0.40;
    public bool ShowSizeLabel { get; set; } = true;

    public void Normalize()
    {
        var defaults = new AppearanceSettings();
        TintColor = NormalizeColor(TintColor, defaults.TintColor);
        LineColor = NormalizeColor(LineColor, defaults.LineColor);
        SelectionColor = NormalizeColor(SelectionColor, defaults.SelectionColor);
        TintOpacity = ClampOpacity(TintOpacity, defaults.TintOpacity);
        LineOpacity = ClampOpacity(LineOpacity, defaults.LineOpacity);
        SelectionOpacity = ClampOpacity(SelectionOpacity, defaults.SelectionOpacity);
    }

    /// <summary>True for a colour written as #RRGGBB.</summary>
    public static bool IsValidColor(string? value) =>
        value is { Length: 7 } && value[0] == '#' && value.AsSpan(1).IndexOfAnyExcept("0123456789abcdefABCDEF") < 0;

    private static string NormalizeColor(string? value, string fallback) =>
        IsValidColor(value) ? value!.ToUpperInvariant() : fallback;

    private static double ClampOpacity(double value, double fallback) =>
        double.IsFinite(value) ? Math.Clamp(value, 0, 1) : fallback;
}

/// <summary>Everything the user can configure. Stored as JSON next to the executable.</summary>
public sealed class Settings
{
    public const int MaxGridSize = 24;
    public const int MaxGap = 200;

    public int Columns { get; set; } = 4;
    public int Rows { get; set; } = 4;
    public int WindowGap { get; set; }
    public int EdgeGap { get; set; }
    public SnapModifier SnapMoveModifier { get; set; } = SnapModifier.Ctrl;
    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>Forces every value into its valid range so a hand-edited file cannot break the app.</summary>
    public void Normalize()
    {
        Columns = Math.Clamp(Columns, 1, MaxGridSize);
        Rows = Math.Clamp(Rows, 1, MaxGridSize);
        WindowGap = Math.Clamp(WindowGap, 0, MaxGap);
        EdgeGap = Math.Clamp(EdgeGap, 0, MaxGap);
        if (!Enum.IsDefined(SnapMoveModifier))
        {
            SnapMoveModifier = SnapModifier.Ctrl;
        }

        Appearance ??= new AppearanceSettings();
        Appearance.Normalize();
    }

    /// <summary>Copies every value from another instance, keeping this object's identity.</summary>
    public void CopyFrom(Settings other)
    {
        Columns = other.Columns;
        Rows = other.Rows;
        WindowGap = other.WindowGap;
        EdgeGap = other.EdgeGap;
        SnapMoveModifier = other.SnapMoveModifier;
        Appearance = new AppearanceSettings
        {
            TintColor = other.Appearance.TintColor,
            TintOpacity = other.Appearance.TintOpacity,
            LineColor = other.Appearance.LineColor,
            LineOpacity = other.Appearance.LineOpacity,
            SelectionColor = other.Appearance.SelectionColor,
            SelectionOpacity = other.Appearance.SelectionOpacity,
            ShowSizeLabel = other.Appearance.ShowSizeLabel,
        };
        Normalize();
    }
}
