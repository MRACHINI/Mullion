using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Mullion.Core;

namespace Mullion.UI;

/// <summary>
/// What the settings window binds to. Every change is written straight into the
/// shared <see cref="Settings"/> object and announced through <see cref="Changed"/>.
/// </summary>
internal sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly Settings _settings;
    private string _tintColor;
    private string _lineColor;
    private string _selectionColor;
    private bool _arrangeShortcutTakenOver;

    public SettingsViewModel(Settings settings)
    {
        _settings = settings;
        _tintColor = settings.Appearance.TintColor;
        _lineColor = settings.Appearance.LineColor;
        _selectionColor = settings.Appearance.SelectionColor;
        RebuildZoneRows();
        RebuildRuleRows();
        RebuildExclusions();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised after any setting changed.</summary>
    public event Action? Changed;

    public static IReadOnlyList<SnapModifier> SnapModifiers { get; } =
        [SnapModifier.None, SnapModifier.Ctrl, SnapModifier.Shift, SnapModifier.Alt];

    public int MaxGridSize => Settings.MaxGridSize;

    public string Version =>
        "Version " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.0.0");

    public int Columns
    {
        get => _settings.Columns;
        set => Set(_settings.Columns, value, v => _settings.Columns = v);
    }

    public int Rows
    {
        get => _settings.Rows;
        set => Set(_settings.Rows, value, v => _settings.Rows = v);
    }

    public int WindowGap
    {
        get => _settings.WindowGap;
        set => Set(_settings.WindowGap, value, v => _settings.WindowGap = v);
    }

    public int EdgeGap
    {
        get => _settings.EdgeGap;
        set => Set(_settings.EdgeGap, value, v => _settings.EdgeGap = v);
    }

    public SnapModifier SnapMoveModifier
    {
        get => _settings.SnapMoveModifier;
        set => Set(_settings.SnapMoveModifier, value, v => _settings.SnapMoveModifier = v);
    }

    public double TintOpacity
    {
        get => _settings.Appearance.TintOpacity;
        set => Set(_settings.Appearance.TintOpacity, value, v => _settings.Appearance.TintOpacity = v);
    }

    public double LineOpacity
    {
        get => _settings.Appearance.LineOpacity;
        set => Set(_settings.Appearance.LineOpacity, value, v => _settings.Appearance.LineOpacity = v);
    }

    public double SelectionOpacity
    {
        get => _settings.Appearance.SelectionOpacity;
        set => Set(_settings.Appearance.SelectionOpacity, value, v => _settings.Appearance.SelectionOpacity = v);
    }

    public bool ShowSizeLabel
    {
        get => _settings.Appearance.ShowSizeLabel;
        set => Set(_settings.Appearance.ShowSizeLabel, value, v => _settings.Appearance.ShowSizeLabel = v);
    }

    // Colour boxes keep whatever the user is typing; the setting itself only
    // changes once the text is a complete #RRGGBB colour.
    public string TintColor
    {
        get => _tintColor;
        set => SetColor(ref _tintColor, value, v => _settings.Appearance.TintColor = v, nameof(TintBrush));
    }

    public string LineColor
    {
        get => _lineColor;
        set => SetColor(ref _lineColor, value, v => _settings.Appearance.LineColor = v, nameof(LineBrush));
    }

    public string SelectionColor
    {
        get => _selectionColor;
        set => SetColor(ref _selectionColor, value, v => _settings.Appearance.SelectionColor = v, nameof(SelectionBrush));
    }

    public Brush TintBrush => ToBrush(_settings.Appearance.TintColor);
    public Brush LineBrush => ToBrush(_settings.Appearance.LineColor);
    public Brush SelectionBrush => ToBrush(_settings.Appearance.SelectionColor);

    public bool StartWithWindows
    {
        get => AutoStart.IsEnabled;
        set
        {
            if (value != AutoStart.IsEnabled)
            {
                AutoStart.SetEnabled(value);
                Raise();
            }
        }
    }

    /// <summary>The zones, one row each, in the order they are stored.</summary>
    public ObservableCollection<ZoneRow> Zones { get; } = [];

    public bool HasZones => Zones.Count > 0;

    /// <summary>
    /// Starts system-wide recording of a shortcut, handing each combination to the
    /// callback; null stops it. Existing shortcuts do not fire meanwhile.
    /// </summary>
    public Action<Action<Hotkey>?> SetRecorder { get; set; } = _ => { };

    /// <summary>Tells whether another program already owns a shortcut.</summary>
    public Func<Hotkey, bool> IsOwnedElsewhere { get; set; } = _ => false;

    public bool CycleMonitors
    {
        get => _settings.CycleMonitors;
        set => Set(_settings.CycleMonitors, value, v => _settings.CycleMonitors = v);
    }

    /// <summary>The programs Mullion leaves alone, in alphabetical order.</summary>
    public ObservableCollection<string> ExcludedPrograms { get; } = [];

    public bool HasExclusions => ExcludedPrograms.Count > 0;

    /// <summary>Lists the programs that have a window open now, for choosing one to exclude.</summary>
    public Func<IReadOnlyList<string>> GetOpenPrograms { get; set; } = () => [];

    /// <summary>
    /// Adds a program to the ones Mullion leaves alone. Returns false when the name
    /// is empty or the program is already on the list.
    /// </summary>
    public bool AddExclusion(string program)
    {
        string name = PlacementRules.NormalizeProgram(program);
        if (name.Length == 0 || _settings.IsExcluded(name))
        {
            return false;
        }

        _settings.ExcludedPrograms.Add(name);
        ExclusionsChanged();
        return true;
    }

    public void RemoveExclusion(string program)
    {
        if (_settings.ExcludedPrograms.RemoveAll(name => string.Equals(name, program, StringComparison.OrdinalIgnoreCase)) > 0)
        {
            ExclusionsChanged();
        }
    }

    private void ExclusionsChanged()
    {
        _settings.Normalize();
        RebuildExclusions();
        Changed?.Invoke();
    }

    private void RebuildExclusions()
    {
        ExcludedPrograms.Clear();
        foreach (string name in _settings.ExcludedPrograms)
        {
            ExcludedPrograms.Add(name);
        }

        Raise(nameof(HasExclusions));
    }

    /// <summary>The placement rules, one row each.</summary>
    public ObservableCollection<RuleRow> Rules { get; } = [];

    public bool HasRules => Rules.Count > 0;

    /// <summary>The zones a rule can point at.</summary>
    public IReadOnlyList<Zone> ZoneChoices => _settings.Zones;

    /// <summary>Applies the rules to every open window now.</summary>
    public Action ApplyRulesNow { get; set; } = () => { };

    /// <summary>Lists the open windows as program and title, for choosing one in the rule editor.</summary>
    public Func<IReadOnlyList<(string Program, string Title)>> GetOpenWindows { get; set; } = () => [];

    /// <summary>How many monitors are connected.</summary>
    public Func<int> GetMonitorCount { get; set; } = () => 1;

    public bool ApplyRulesOnOpen
    {
        get => _settings.ApplyRulesOnOpen;
        set => Set(_settings.ApplyRulesOnOpen, value, v => _settings.ApplyRulesOnOpen = v);
    }

    public bool RememberPlacements
    {
        get => _settings.RememberPlacements;
        set => Set(_settings.RememberPlacements, value, v => _settings.RememberPlacements = v);
    }

    /// <summary>True when there is at least one placement Mullion remembered by itself.</summary>
    public bool HasRemembered => _settings.Rules.Any(rule => rule.Learned);

    /// <summary>Removes every placement Mullion remembered by itself; rules the user wrote stay.</summary>
    public void ForgetRemembered()
    {
        if (_settings.Rules.RemoveAll(rule => rule.Learned) > 0)
        {
            RulesChanged();
        }
    }

    /// <summary>Shows the rules again after they were changed from outside this window.</summary>
    public void RefreshRules() => RebuildRuleRows();

    /// <summary>A starting point for a new rule: the first zone, on whichever monitor the window opens.</summary>
    public PlacementRule NewRule() => new() { ZoneId = _settings.Zones.FirstOrDefault()?.Id ?? string.Empty };

    public void AddRule(PlacementRule rule)
    {
        _settings.Rules.Add(rule);
        RulesChanged();
    }

    public void ReplaceRule(RuleRow row, PlacementRule rule)
    {
        int index = _settings.Rules.IndexOf(row.Rule);
        if (index >= 0)
        {
            _settings.Rules[index] = rule;
            RulesChanged();
        }
    }

    public void RemoveRule(RuleRow row)
    {
        if (_settings.Rules.Remove(row.Rule))
        {
            RulesChanged();
        }
    }

    private void RulesChanged()
    {
        RebuildRuleRows();
        Changed?.Invoke();
    }

    private void RebuildRuleRows()
    {
        Rules.Clear();
        foreach (var rule in _settings.Rules)
        {
            Rules.Add(new RuleRow(rule, PlacementRules.TargetOf(rule, _settings.FindZone), () => Changed?.Invoke()));
        }

        Raise(nameof(HasRules));
        Raise(nameof(HasRemembered));
    }

    /// <summary>Starts arranging the open windows one by one.</summary>
    public Action StartArranging { get; set; } = () => { };

    /// <summary>The shortcut that starts arranging windows; null for none.</summary>
    public Hotkey? ArrangeHotkey
    {
        get => _settings.ArrangeHotkey;
        set
        {
            if (_settings.ArrangeHotkey == value)
            {
                return;
            }

            // A shortcut can do one thing; giving it to this takes it from any zone.
            if (value is { } hotkey)
            {
                foreach (var zone in _settings.Zones.Where(zone => zone.Hotkey == hotkey))
                {
                    zone.Shortcut = string.Empty;
                }
            }

            _settings.ArrangeShortcut = value?.ToString() ?? string.Empty;
            ArrangeShortcutTakenOver = false;
            Raise();
            ZonesChanged();
        }
    }

    /// <summary>True when another program owns the arrange shortcut and Mullion has taken it over.</summary>
    public bool ArrangeShortcutTakenOver
    {
        get => _arrangeShortcutTakenOver;
        private set
        {
            if (_arrangeShortcutTakenOver != value)
            {
                _arrangeShortcutTakenOver = value;
                Raise();
            }
        }
    }

    /// <summary>A starting point for a new zone: the left half of the main grid.</summary>
    public Zone NewZone() => new()
    {
        Name = $"Zone {_settings.Zones.Count + 1}",
        Columns = _settings.Columns,
        Rows = _settings.Rows,
        Column0 = 0,
        Row0 = 0,
        Column1 = Math.Max(0, (_settings.Columns / 2) - 1),
        Row1 = _settings.Rows - 1,
    };

    /// <summary>The zones other than the one shown in a row; all zones when the row is null.</summary>
    public IReadOnlyList<Zone> ZonesExcept(ZoneRow? row) =>
        _settings.Zones.Where(zone => !ReferenceEquals(zone, row?.Zone)).ToList();

    public void AddZone(Zone zone)
    {
        TakeShortcutFor(zone, except: null);
        _settings.Zones.Add(zone);
        ZonesChanged();
    }

    public void ReplaceZone(ZoneRow row, Zone zone)
    {
        int index = _settings.Zones.IndexOf(row.Zone);
        if (index < 0)
        {
            return;
        }

        TakeShortcutFor(zone, except: row.Zone);
        _settings.Zones[index] = zone;
        ZonesChanged();
    }

    public void RemoveZone(ZoneRow row)
    {
        if (_settings.Zones.Remove(row.Zone))
        {
            ZonesChanged();
        }
    }

    /// <summary>Marks the zones, by position, whose shortcut was taken over from another program.</summary>
    public void SetTakenOverShortcuts(IReadOnlyList<int> positions)
    {
        for (int i = 0; i < Zones.Count; i++)
        {
            Zones[i].ShortcutTakenOver = positions.Contains(i);
        }

        // The arrange shortcut is registered straight after the zones' shortcuts.
        ArrangeShortcutTakenOver = positions.Contains(Zones.Count);
    }

    /// <summary>A shortcut belongs to one zone; giving it to a zone takes it from any other.</summary>
    private void TakeShortcutFor(Zone zone, Zone? except)
    {
        if (zone.Hotkey is not { } hotkey)
        {
            return;
        }

        foreach (var other in _settings.Zones)
        {
            if (!ReferenceEquals(other, except) && other.Hotkey == hotkey)
            {
                other.Shortcut = string.Empty;
            }
        }
    }

    private void ZonesChanged()
    {
        RebuildZoneRows();

        // Rule rows show zone names, and a deleted zone leaves its rules pointing nowhere.
        RebuildRuleRows();
        Changed?.Invoke();
    }

    private void RebuildZoneRows()
    {
        Zones.Clear();
        foreach (var zone in _settings.Zones)
        {
            Zones.Add(new ZoneRow(zone));
        }

        Raise(nameof(HasZones));
    }

    public void ResetToDefaults()
    {
        // Zones, rules and exclusions are the user's own work, not options;
        // resetting keeps them.
        var defaults = new Settings
        {
            Zones = _settings.Zones,
            Rules = _settings.Rules,
            ExcludedPrograms = _settings.ExcludedPrograms,
            CycleMonitors = _settings.CycleMonitors,
        };
        _settings.CopyFrom(defaults);
        RebuildZoneRows();
        RebuildRuleRows();
        RebuildExclusions();
        _tintColor = _settings.Appearance.TintColor;
        _lineColor = _settings.Appearance.LineColor;
        _selectionColor = _settings.Appearance.SelectionColor;

        // An empty name tells WPF that every property may have changed.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        Changed?.Invoke();
    }

    private void Set<T>(T current, T value, Action<T> assign, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(current, value))
        {
            return;
        }

        assign(value);
        Raise(name);
        Changed?.Invoke();
    }

    private void SetColor(ref string field, string value, Action<string> assign, string brushName, [CallerMemberName] string? name = null)
    {
        value = (value ?? string.Empty).Trim();
        if (field == value)
        {
            return;
        }

        field = value;
        Raise(name);

        if (AppearanceSettings.IsValidColor(value))
        {
            assign(value.ToUpperInvariant());
            Raise(brushName);
            Changed?.Invoke();
        }
    }

    private void Raise([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private static Brush ToBrush(string hex)
    {
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
        catch (FormatException)
        {
            return Brushes.Transparent;
        }
    }
}
