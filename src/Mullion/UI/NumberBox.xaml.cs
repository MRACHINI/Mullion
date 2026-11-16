using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Mullion.UI;

/// <summary>
/// A box for a whole number with up and down arrows. The number can be typed,
/// stepped with the arrows, the Up and Down keys, or the mouse wheel while the
/// box has the focus. It never leaves its range.
/// </summary>
internal partial class NumberBox : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(int),
        typeof(NumberBox),
        new FrameworkPropertyMetadata(
            0,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            (d, _) => ((NumberBox)d).ShowValue()));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(int),
        typeof(NumberBox),
        new PropertyMetadata(0, (d, _) => ((NumberBox)d).UpdateArrows()));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(int),
        typeof(NumberBox),
        new PropertyMetadata(100, (d, _) => ((NumberBox)d).UpdateArrows()));

    private static readonly Brush NormalBorder = new SolidColorBrush(Color.FromRgb(0x9C, 0xA3, 0xAF));
    private static readonly Brush FocusBorder = new SolidColorBrush(Color.FromRgb(0x25, 0x63, 0xEB));

    private bool _syncing;

    public NumberBox()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(Box, OnPaste);
        ShowValue();
    }

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public int Minimum
    {
        get => (int)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public int Maximum
    {
        get => (int)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    private static bool IsDigits(string text) => text.Length > 0 && text.All(c => c is >= '0' and <= '9');

    private int Clamp(int value) => Math.Clamp(value, Minimum, Math.Max(Minimum, Maximum));

    /// <summary>Sets the value through the binding, so the bound setting changes too.</summary>
    private void Set(int value)
    {
        SetCurrentValue(ValueProperty, Clamp(value));
        ShowValue();
    }

    private void Step(int delta)
    {
        // Step from what is typed when that is a number, so "7" then Up gives 8.
        int from = int.TryParse(Box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int typed) ? typed : Value;
        Set(from + delta);
        Box.CaretIndex = Box.Text.Length;
    }

    private void ShowValue()
    {
        string text = Value.ToString(CultureInfo.InvariantCulture);
        if (Box.Text != text)
        {
            _syncing = true;
            Box.Text = text;
            _syncing = false;
        }

        UpdateArrows();
    }

    private void UpdateArrows()
    {
        UpButton.IsEnabled = Value < Maximum;
        DownButton.IsEnabled = Value > Minimum;
    }

    /// <summary>Accepts what was typed, pulling it into range, or puts the old number back.</summary>
    private void Commit()
    {
        if (int.TryParse(Box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int typed))
        {
            Set(typed);
        }
        else
        {
            ShowValue();
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing)
        {
            return;
        }

        // Apply a number as soon as it is valid; anything else waits for Enter or
        // for the box to lose the focus, so "1" on the way to "12" is not corrected.
        if (int.TryParse(Box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out int typed)
            && typed >= Minimum
            && typed <= Maximum)
        {
            SetCurrentValue(ValueProperty, typed);
            UpdateArrows();
        }
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        e.Handled = !IsDigits(e.Text);
    }

    private void OnPaste(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetData(DataFormats.UnicodeText) is not string text || !IsDigits(text.Trim()))
        {
            e.CancelCommand();
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Step(1);
                e.Handled = true;
                break;

            case Key.Down:
                Step(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                Commit();
                Box.SelectAll();
                e.Handled = true;
                break;

            case Key.Space:
                e.Handled = true;
                break;
        }
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Box.IsKeyboardFocused)
        {
            Step(Math.Sign(e.Delta));
            e.Handled = true;
        }
    }

    private void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Frame.BorderBrush = FocusBorder;
        Box.SelectAll();
    }

    private void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        Frame.BorderBrush = NormalBorder;
        Commit();
    }

    private void OnUpClick(object sender, RoutedEventArgs e) => Step(1);

    private void OnDownClick(object sender, RoutedEventArgs e) => Step(-1);
}
