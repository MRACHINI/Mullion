namespace Mullion.Core;

/// <summary>The modifier keys of a shortcut. The values match the ones Windows uses.</summary>
[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A keyboard shortcut: modifier keys plus one other key, identified by its
/// Windows virtual-key code. Written as text like "Ctrl+Alt+1".
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, int Key)
{
    private static readonly (HotkeyModifiers Modifier, string Name)[] ModifierNames =
    [
        (HotkeyModifiers.Ctrl, "Ctrl"),
        (HotkeyModifiers.Alt, "Alt"),
        (HotkeyModifiers.Shift, "Shift"),
        (HotkeyModifiers.Win, "Win"),
    ];

    private static readonly Dictionary<int, string> NamesByKey = BuildKeyNames();

    private static readonly Dictionary<string, int> KeysByName =
        NamesByKey.ToDictionary(pair => pair.Value, pair => pair.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when the shortcut can be used system-wide: a key Mullion knows, together
    /// with Ctrl, Alt or Win. Shift alone is not enough, because Shift+letter is typing.
    /// </summary>
    public bool IsValid =>
        NamesByKey.ContainsKey(Key)
        && (Modifiers & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) != 0;

    /// <summary>True for Ctrl, Alt, Shift and the Windows key, in their left, right and generic forms.</summary>
    public static bool IsModifierKey(int key) => key is
        0x10 or 0x11 or 0x12 or      // Shift, Ctrl, Alt
        0x5B or 0x5C or              // left and right Windows key
        (>= 0xA0 and <= 0xA5);       // left and right Shift, Ctrl, Alt

    /// <summary>The name of a key, or null for a key that cannot be part of a shortcut.</summary>
    public static string? KeyName(int key) => NamesByKey.GetValueOrDefault(key);

    public override string ToString()
    {
        var parts = new List<string>(5);
        foreach (var (modifier, name) in ModifierNames)
        {
            if ((Modifiers & modifier) != 0)
            {
                parts.Add(name);
            }
        }

        parts.Add(KeyName(Key) ?? $"Key{Key}");
        return string.Join('+', parts);
    }

    /// <summary>Reads text such as "ctrl+alt+1". Order and case of the modifiers do not matter.</summary>
    public static bool TryParse(string? text, out Hotkey hotkey)
    {
        hotkey = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // No key name contains a space, so spaces can simply go.
        string rest = text.Replace(" ", string.Empty);
        var modifiers = HotkeyModifiers.None;

        // Peel modifiers off the front; what remains is the key, which may itself
        // contain a plus sign ("Num+").
        bool found = true;
        while (found)
        {
            found = false;
            foreach (var (modifier, name) in ModifierNames)
            {
                if (rest.Length > name.Length
                    && rest[name.Length] == '+'
                    && rest.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= modifier;
                    rest = rest[(name.Length + 1)..];
                    found = true;
                }
            }
        }

        if (!KeysByName.TryGetValue(rest, out int key))
        {
            return false;
        }

        hotkey = new Hotkey(modifiers, key);
        return true;
    }

    private static Dictionary<int, string> BuildKeyNames()
    {
        var names = new Dictionary<int, string>();

        for (int i = 0; i <= 9; i++)
        {
            names[0x30 + i] = i.ToString();
            names[0x60 + i] = "Num" + i;
        }

        for (int i = 0; i < 26; i++)
        {
            names[0x41 + i] = ((char)('A' + i)).ToString();
        }

        for (int i = 1; i <= 24; i++)
        {
            names[0x70 + i - 1] = "F" + i;
        }

        names[0x6A] = "Num*";
        names[0x6B] = "Num+";
        names[0x6D] = "Num-";
        names[0x6E] = "Num.";
        names[0x6F] = "Num/";

        names[0x25] = "Left";
        names[0x26] = "Up";
        names[0x27] = "Right";
        names[0x28] = "Down";
        names[0x21] = "PageUp";
        names[0x22] = "PageDown";
        names[0x23] = "End";
        names[0x24] = "Home";
        names[0x2D] = "Insert";
        names[0x2E] = "Delete";
        names[0x20] = "Space";
        names[0x09] = "Tab";
        names[0x0D] = "Enter";

        names[0xBA] = ";";
        names[0xBB] = "=";
        names[0xBC] = ",";
        names[0xBD] = "-";
        names[0xBE] = ".";
        names[0xBF] = "/";
        names[0xC0] = "`";
        names[0xDB] = "[";
        names[0xDC] = "\\";
        names[0xDD] = "]";
        names[0xDE] = "'";

        return names;
    }
}
