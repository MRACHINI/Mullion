using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mullion.Core;

namespace Mullion.UI;

/// <summary>The dialog for creating or changing one zone.</summary>
internal partial class ZoneEditorWindow : Window
{
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x6B, 0x72, 0x80));
    private static readonly Brush Warning = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09));

    private const string DefaultHint = "Click the box and press the keys, for example Ctrl+Alt+1.";

    private readonly IReadOnlyList<Zone> _otherZones;
    private readonly Action<Action<Hotkey>?> _setRecorder;
    private readonly Func<Hotkey, bool> _isOwnedElsewhere;

    /// <param name="zone">The zone to edit; it is copied, not changed.</param>
    /// <param name="otherZones">The other zones, to warn about a shortcut that is already taken.</param>
    /// <param name="setRecorder">
    /// Starts system-wide recording while the shortcut box waits for keys, handing
    /// each combination to the given callback; null stops it.
    /// </param>
    /// <param name="isOwnedElsewhere">Tells whether another program already owns a shortcut.</param>
    public ZoneEditorWindow(
        Zone zone,
        IReadOnlyList<Zone> otherZones,
        Action<Action<Hotkey>?> setRecorder,
        Func<Hotkey, bool> isOwnedElsewhere)
    {
        _otherZones = otherZones;
        _setRecorder = setRecorder;
        _isOwnedElsewhere = isOwnedElsewhere;

        InitializeComponent();

        Zone = zone.Clone();
        Zone.Normalize();

        NameBox.Text = Zone.Name;
        ColumnsBox.Maximum = Settings.MaxGridSize;
        RowsBox.Maximum = Settings.MaxGridSize;
        ColumnsBox.Value = Zone.Columns;
        RowsBox.Value = Zone.Rows;
        Picker.Columns = Zone.Columns;
        Picker.Rows = Zone.Rows;
        Picker.Span = Zone.Span;
        ShortcutBox.Hotkey = Zone.Hotkey;

        Watch(ColumnsBox, NumberBox.ValueProperty, OnGridChanged);
        Watch(RowsBox, NumberBox.ValueProperty, OnGridChanged);
        Watch(Picker, ZonePicker.SpanProperty, UpdateSelectionText);
        Watch(ShortcutBox, HotkeyBox.HotkeyProperty, CheckShortcut);

        ShortcutBox.Rejected += reason => ShowHint(reason, isWarning: true);
        ShortcutBox.Accepted += CheckShortcut;
        ShortcutBox.GotKeyboardFocus += (_, _) => _setRecorder(ShortcutBox.Accept);
        ShortcutBox.LostKeyboardFocus += (_, _) => _setRecorder(null);
        Closed += (_, _) => _setRecorder(null);

        UpdateSelectionText();
        CheckShortcut();

        Loaded += (_, _) =>
        {
            NameBox.Focus();
            NameBox.SelectAll();
        };
    }

    /// <summary>The edited zone. Valid after the dialog was closed with Save.</summary>
    public Zone Zone { get; }

    private static void Watch(DependencyObject target, DependencyProperty property, Action changed)
    {
        DependencyPropertyDescriptor
            .FromProperty(property, target.GetType())
            .AddValueChanged(target, (_, _) => changed());
    }

    private void OnGridChanged()
    {
        Picker.Columns = ColumnsBox.Value;
        Picker.Rows = RowsBox.Value;

        // Keep the selection inside the new grid.
        var span = Picker.Span;
        int lastColumn = Picker.Columns - 1;
        int lastRow = Picker.Rows - 1;
        Picker.Span = new CellSpan(
            Math.Min(span.Column0, lastColumn),
            Math.Min(span.Row0, lastRow),
            Math.Min(span.Column1, lastColumn),
            Math.Min(span.Row1, lastRow));

        UpdateSelectionText();
    }

    private void UpdateSelectionText()
    {
        var span = Picker.Span;
        string cells = span.Columns == Picker.Columns && span.Rows == Picker.Rows
            ? "the whole screen"
            : $"{span.Columns} × {span.Rows} of {Picker.Columns} × {Picker.Rows} cells";
        SelectionText.Text = $"Selected: {cells}. Drag across the picture to change it.";
    }

    private void CheckShortcut()
    {
        if (ShortcutBox.Hotkey is { } hotkey
            && _otherZones.FirstOrDefault(zone => zone.Hotkey == hotkey) is { } owner)
        {
            string name = string.IsNullOrWhiteSpace(owner.Name) ? "another zone" : $"“{owner.Name}”";
            ShowHint($"{hotkey} is already used by {name}. Saving will move it to this zone.", isWarning: true);
        }
        else if (ShortcutBox.Hotkey is { } taken && _isOwnedElsewhere(taken))
        {
            ShowHint(
                $"Another program already uses {taken}. Mullion will take it over while Mullion is running, so that program will not receive it.",
                isWarning: true);
        }
        else
        {
            ShowHint(DefaultHint, isWarning: false);
        }
    }

    private void ShowHint(string text, bool isWarning)
    {
        ShortcutHint.Text = text;
        ShortcutHint.Foreground = isWarning ? Warning : Muted;
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        ShortcutBox.Hotkey = null;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        // Enter inside a number box has not left the box yet; make it take its value.
        SaveButton.Focus();
        Keyboard.ClearFocus();

        Zone.Name = NameBox.Text;
        Zone.Columns = ColumnsBox.Value;
        Zone.Rows = RowsBox.Value;
        Zone.Span = Picker.Span;
        Zone.Shortcut = ShortcutBox.Hotkey?.ToString() ?? string.Empty;
        Zone.Normalize();

        DialogResult = true;
    }
}
