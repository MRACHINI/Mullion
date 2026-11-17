namespace Mullion.Core;

/// <summary>The key that turns an ordinary window drag into a move on the grid.</summary>
public enum SnapModifier
{
    None,
    Ctrl,
    Shift,
    Alt,
}

/// <summary>
/// A mouse button other than the left one, which drags the window. One of them
/// shows the grid during a drag.
/// </summary>
public enum TriggerButton
{
    Right,
    Middle,

    /// <summary>The first side button, which browsers use for "back".</summary>
    Back,

    /// <summary>The second side button, which browsers use for "forward".</summary>
    Forward,
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
    public const int MaxGridSize = 100;

    /// <summary>
    /// The finest grid an area can be described on. It is larger than the main
    /// grid can be because the mouse wheel once multiplied the grid, and areas
    /// remembered then are still read.
    /// </summary>
    public const int MaxAreaGridSize = MaxGridSize * 4;
    public const int MaxGap = 200;

    public const int DefaultGridSize = 60;

    /// <summary>The largest grid a new zone starts on, so its cells are easy to pick.</summary>
    public const int NewZoneGridSize = 12;

    public int Columns { get; set; } = DefaultGridSize;
    public int Rows { get; set; } = DefaultGridSize;
    public int WindowGap { get; set; }
    public int EdgeGap { get; set; }
    public SnapModifier SnapMoveModifier { get; set; } = SnapModifier.Ctrl;

    /// <summary>The mouse button that shows the grid while a window is being dragged.</summary>
    public TriggerButton GridButton { get; set; } = TriggerButton.Right;

    /// <summary>
    /// The key that abandons the grid, by name: one key on its own, such as "Esc",
    /// "Q" or "F2".
    /// </summary>
    public string CancelKey { get; set; } = DefaultCancelKey;

    public const string DefaultCancelKey = "Esc";

    /// <summary>The virtual-key code of <see cref="CancelKey"/>.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public int CancelKeyCode => Hotkey.TryParseSingleKey(CancelKey, out int key) ? key : Hotkey.EscapeKey;

    public AppearanceSettings Appearance { get; set; } = new();

    /// <summary>Saved zones, in the order the user arranged them.</summary>
    public List<Zone> Zones { get; set; } = [];

    /// <summary>Pressing a zone shortcut again moves the window to the next monitor.</summary>
    public bool CycleMonitors { get; set; } = true;

    /// <summary>Rules that put a window in a zone when it opens.</summary>
    public List<PlacementRule> Rules { get; set; } = [];

    /// <summary>Whether the rules are applied when a window opens.</summary>
    public bool ApplyRulesOnOpen { get; set; } = true;

    /// <summary>
    /// Whether placing a window with Mullion (the grid gesture, a zone shortcut or
    /// arranging) is remembered, so the program's next window opens in the same place.
    /// </summary>
    public bool RememberPlacements { get; set; } = true;

    /// <summary>
    /// Programs Mullion leaves alone, by name as Task Manager shows it, without
    /// ".exe". Their windows get no grid, no shortcuts, no arranging, and nothing
    /// is remembered or placed for them.
    /// </summary>
    public List<string> ExcludedPrograms { get; set; } = [];

    /// <summary>True when Mullion is to leave the given program alone.</summary>
    public bool IsExcluded(string program) =>
        ExcludedPrograms.Contains(PlacementRules.NormalizeProgram(program), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The shortcut, as text, that starts arranging the open windows one by one;
    /// empty for none.
    /// </summary>
    public string ArrangeShortcut { get; set; } = string.Empty;

    /// <summary>
    /// Whether Mullion runs as administrator, so that it can also move the windows
    /// of programs that run as administrator.
    /// </summary>
    public bool RunAsAdministrator { get; set; }

    /// <summary>Whether Mullion asks GitHub, once a day, if a newer version has been published.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>The arrange shortcut, or null when there is none or it cannot be used.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Hotkey? ArrangeHotkey =>
        Hotkey.TryParse(ArrangeShortcut, out var hotkey) && hotkey.IsValid ? hotkey : null;

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

        if (!Enum.IsDefined(GridButton))
        {
            GridButton = TriggerButton.Right;
        }

        // Written the one way Mullion writes it, or back to Esc when it names no key.
        CancelKey = Hotkey.TryParseSingleKey(CancelKey, out int cancel)
            ? Hotkey.SingleKeyName(cancel)!
            : DefaultCancelKey;

        Appearance ??= new AppearanceSettings();
        Appearance.Normalize();

        Zones ??= [];
        Zones.RemoveAll(zone => zone is null);

        // Two zones cannot share a shortcut; the first one keeps it. Nor an identifier:
        // a copied zone gets a new one.
        var used = new HashSet<Hotkey>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var zone in Zones)
        {
            zone.Normalize();
            while (!ids.Add(zone.Id))
            {
                zone.Id = Zone.NewId();
            }

            if (zone.Hotkey is { } hotkey && !used.Add(hotkey))
            {
                zone.Shortcut = string.Empty;
            }
        }

        ArrangeShortcut = ArrangeHotkey is { } arrange && used.Add(arrange) ? arrange.ToString() : string.Empty;

        // A rule without a program can never match. A rule whose zone was deleted is
        // kept, so the user can see it and point it at another zone.
        Rules ??= [];
        Rules.RemoveAll(rule => rule is null);
        foreach (var rule in Rules)
        {
            rule.Normalize();
        }

        Rules.RemoveAll(rule => rule.Program.Length == 0);

        // One tidy entry per program, whatever its case or ".exe", in alphabetical order.
        ExcludedPrograms = (ExcludedPrograms ?? [])
            .Select(PlacementRules.NormalizeProgram)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>The zone with the given identifier, or null.</summary>
    public Zone? FindZone(string id) => Zones.FirstOrDefault(zone => zone.Id == id);

    /// <summary>Copies every value from another instance, keeping this object's identity.</summary>
    public void CopyFrom(Settings other)
    {
        Columns = other.Columns;
        Rows = other.Rows;
        WindowGap = other.WindowGap;
        EdgeGap = other.EdgeGap;
        SnapMoveModifier = other.SnapMoveModifier;
        GridButton = other.GridButton;
        CancelKey = other.CancelKey;
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
        Zones = other.Zones.Select(zone => zone.Clone()).ToList();
        CycleMonitors = other.CycleMonitors;
        ArrangeShortcut = other.ArrangeShortcut;
        Rules = other.Rules.Select(rule => rule.Clone()).ToList();
        ApplyRulesOnOpen = other.ApplyRulesOnOpen;
        RememberPlacements = other.RememberPlacements;
        ExcludedPrograms = [.. other.ExcludedPrograms];
        RunAsAdministrator = other.RunAsAdministrator;
        CheckForUpdates = other.CheckForUpdates;
        Normalize();
    }
}
