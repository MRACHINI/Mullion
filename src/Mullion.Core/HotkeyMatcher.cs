namespace Mullion.Core;

/// <summary>What to do with one key press seen by the keyboard hook.</summary>
/// <param name="Swallow">Hide the key from every other program.</param>
/// <param name="Zone">The zone whose shortcut was pressed, if any.</param>
/// <param name="Captured">The shortcut the user pressed while one was being recorded, if any.</param>
public readonly record struct KeyDecision(bool Swallow, int? Zone = null, Hotkey? Captured = null);

/// <summary>
/// Recognises shortcuts from raw key presses. Used for shortcuts Windows will not
/// register because another program already owns them, and for recording a new
/// shortcut, where every combination must be seen, including ones owned by others.
///
/// Not thread-safe: call it from the thread that owns the keyboard hook.
/// </summary>
public sealed class HotkeyMatcher
{
    private const HotkeyModifiers Required = HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win;

    private readonly HashSet<int> _swallowed = [];
    private Dictionary<Hotkey, int> _zones = [];

    /// <summary>While true, the next usable combination is reported instead of acted on.</summary>
    public bool Recording { get; set; }

    /// <summary>True when there is nothing to watch for, so no keyboard hook is needed.</summary>
    public bool IsIdle => !Recording && _zones.Count == 0;

    /// <summary>Sets the shortcuts to recognise and the zone each one belongs to.</summary>
    public void SetZones(IReadOnlyDictionary<Hotkey, int> zones)
    {
        _zones = new Dictionary<Hotkey, int>(zones);
    }

    /// <param name="key">Virtual-key code of the key that went down.</param>
    /// <param name="modifiers">Modifier keys held at that moment.</param>
    public KeyDecision KeyDown(int key, HotkeyModifiers modifiers)
    {
        // Holding the key down repeats it; the repeats belong to the press already handled.
        if (_swallowed.Contains(key))
        {
            return new KeyDecision(Swallow: true);
        }

        if (Hotkey.IsModifierKey(key))
        {
            return default;
        }

        var hotkey = new Hotkey(modifiers, key);

        if (Recording)
        {
            // Keys without Ctrl, Alt or Win go on to the shortcut box, which uses
            // them itself (Tab, Esc, Backspace) or explains why they are not enough.
            if ((modifiers & Required) == 0 || Hotkey.KeyName(key) is null)
            {
                return default;
            }

            _swallowed.Add(key);
            return new KeyDecision(Swallow: true, Captured: hotkey);
        }

        if (_zones.TryGetValue(hotkey, out int zone))
        {
            _swallowed.Add(key);
            return new KeyDecision(Swallow: true, Zone: zone);
        }

        return default;
    }

    /// <summary>Returns true when the release belongs to a press that was hidden and must be hidden too.</summary>
    public bool KeyUp(int key) => _swallowed.Remove(key);
}
