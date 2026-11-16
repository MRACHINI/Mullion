using System.ComponentModel;
using Mullion.Core;

namespace Mullion.UI;

/// <summary>One line of the zone list in the settings window.</summary>
internal sealed class ZoneRow : INotifyPropertyChanged
{
    private bool _shortcutRefused;

    public ZoneRow(Zone zone)
    {
        Zone = zone;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Zone Zone { get; }

    public string Name => string.IsNullOrWhiteSpace(Zone.Name) ? "Unnamed zone" : Zone.Name;

    public int Columns => Zone.Columns;
    public int Rows => Zone.Rows;
    public CellSpan Span => Zone.Span;

    public string Description
    {
        get
        {
            var span = Zone.Span;
            return span.Columns == Zone.Columns && span.Rows == Zone.Rows
                ? "Whole screen"
                : $"{span.Columns} × {span.Rows} of {Zone.Columns} × {Zone.Rows} cells";
        }
    }

    public bool HasShortcut => Zone.Hotkey is not null;

    public string ShortcutText => Zone.Hotkey?.ToString() ?? "No shortcut";

    /// <summary>True when Windows would not register the shortcut, usually because another program owns it.</summary>
    public bool ShortcutRefused
    {
        get => _shortcutRefused;
        set
        {
            if (_shortcutRefused != value)
            {
                _shortcutRefused = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ShortcutRefused)));
            }
        }
    }
}
