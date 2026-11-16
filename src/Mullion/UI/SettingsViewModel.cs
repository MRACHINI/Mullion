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

    public SettingsViewModel(Settings settings)
    {
        _settings = settings;
        _tintColor = settings.Appearance.TintColor;
        _lineColor = settings.Appearance.LineColor;
        _selectionColor = settings.Appearance.SelectionColor;
        RebuildZoneRows();
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

    /// <summary>Told true while a shortcut box is waiting for keys, so existing shortcuts do not fire.</summary>
    public Action<bool> SetRecording { get; set; } = _ => { };

    public bool CycleMonitors
    {
        get => _settings.CycleMonitors;
        set => Set(_settings.CycleMonitors, value, v => _settings.CycleMonitors = v);
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

    /// <summary>Marks the zones, by position, whose shortcut Windows would not register.</summary>
    public void SetShortcutProblems(IReadOnlyList<int> positions)
    {
        for (int i = 0; i < Zones.Count; i++)
        {
            Zones[i].ShortcutRefused = positions.Contains(i);
        }
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
        // Zones are the user's own work, not an option; resetting keeps them.
        var defaults = new Settings
        {
            Zones = _settings.Zones,
            CycleMonitors = _settings.CycleMonitors,
        };
        _settings.CopyFrom(defaults);
        RebuildZoneRows();
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
