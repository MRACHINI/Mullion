using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Mullion.Core;

namespace Mullion.UI;

/// <summary>
/// A box that records a keyboard shortcut: click it and press the keys.
/// Backspace or Delete clears it.
/// </summary>
internal sealed class HotkeyBox : TextBox
{
    public static readonly DependencyProperty HotkeyProperty = DependencyProperty.Register(
        nameof(Hotkey),
        typeof(Hotkey?),
        typeof(HotkeyBox),
        new FrameworkPropertyMetadata(
            null,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((HotkeyBox)d).ShowHotkey()));

    public HotkeyBox()
    {
        IsReadOnly = true;
        IsReadOnlyCaretVisible = false;
        IsUndoEnabled = false;
        Cursor = Cursors.Arrow;
        ContextMenu = null;
        InputMethod.SetIsInputMethodEnabled(this, false);
        ShowHotkey();
    }

    /// <summary>Raised when the user pressed keys that cannot be used, with the reason.</summary>
    public event Action<string>? Rejected;

    public Hotkey? Hotkey
    {
        get => (Hotkey?)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }

    protected override void OnGotKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnGotKeyboardFocus(e);
        ShowHotkey();
    }

    protected override void OnLostKeyboardFocus(KeyboardFocusChangedEventArgs e)
    {
        base.OnLostKeyboardFocus(e);
        ShowHotkey();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        // Alt combinations arrive as "system" keys, with the real key in SystemKey.
        Key key = e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            _ => e.Key,
        };

        var modifiers = CurrentModifiers();

        // Tab on its own keeps its usual job of moving to the next control.
        if (key == Key.Tab && (modifiers & ~HotkeyModifiers.Shift) == 0)
        {
            return;
        }

        e.Handled = true;

        if (IsModifierKey(key))
        {
            Text = modifiers == HotkeyModifiers.None ? Prompt : new Hotkey(modifiers, 0).ToString().Replace("Key0", "…");
            return;
        }

        if (modifiers == HotkeyModifiers.None && key is Key.Back or Key.Delete)
        {
            SetCurrentValue(HotkeyProperty, null);
            ShowHotkey();
            return;
        }

        if (modifiers == HotkeyModifiers.None && key == Key.Escape)
        {
            ShowHotkey();
            return;
        }

        var hotkey = new Hotkey(modifiers, KeyInterop.VirtualKeyFromKey(key));
        if (Core.Hotkey.KeyName(hotkey.Key) is null)
        {
            Rejected?.Invoke("That key cannot be used in a shortcut.");
            ShowHotkey();
        }
        else if (!hotkey.IsValid)
        {
            Rejected?.Invoke("A shortcut needs Ctrl, Alt or the Windows key.");
            ShowHotkey();
        }
        else
        {
            SetCurrentValue(HotkeyProperty, hotkey);
            ShowHotkey();
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        // Put the real text back once the modifiers pressed on their own are released.
        if (CurrentModifiers() == HotkeyModifiers.None)
        {
            ShowHotkey();
        }

        base.OnPreviewKeyUp(e);
    }

    private const string Prompt = "Press the shortcut…";

    private void ShowHotkey()
    {
        Text = Hotkey is { } hotkey ? hotkey.ToString() : IsKeyboardFocused ? Prompt : "None";
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or
        Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin;

    private static HotkeyModifiers CurrentModifiers()
    {
        var result = HotkeyModifiers.None;
        var keys = Keyboard.Modifiers;

        if (keys.HasFlag(ModifierKeys.Control))
        {
            result |= HotkeyModifiers.Ctrl;
        }

        if (keys.HasFlag(ModifierKeys.Alt))
        {
            result |= HotkeyModifiers.Alt;
        }

        if (keys.HasFlag(ModifierKeys.Shift))
        {
            result |= HotkeyModifiers.Shift;
        }

        if (keys.HasFlag(ModifierKeys.Windows) || Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin))
        {
            result |= HotkeyModifiers.Win;
        }

        return result;
    }
}
