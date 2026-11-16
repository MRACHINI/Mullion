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

    public void ResetToDefaults()
    {
        _settings.CopyFrom(new Settings());
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
