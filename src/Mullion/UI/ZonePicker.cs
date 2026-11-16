using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mullion.Core;

namespace Mullion.UI;

/// <summary>
/// A small picture of the screen divided into cells. Dragging across cells selects
/// a block. With <see cref="IsReadOnly"/> it only shows a selection, as a thumbnail.
/// </summary>
internal sealed class ZonePicker : FrameworkElement
{
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns), typeof(int), typeof(ZonePicker),
        new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(int), typeof(ZonePicker),
        new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SpanProperty = DependencyProperty.Register(
        nameof(Span), typeof(CellSpan), typeof(ZonePicker),
        new FrameworkPropertyMetadata(
            default(CellSpan),
            FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public static readonly DependencyProperty IsReadOnlyProperty = DependencyProperty.Register(
        nameof(IsReadOnly), typeof(bool), typeof(ZonePicker),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Brush Screen = Frozen(Color.FromRgb(0xE5, 0xE7, 0xEB));
    private static readonly Brush Lines = Frozen(Color.FromRgb(0xB6, 0xBC, 0xC6));
    private static readonly Brush Fill = Frozen(Color.FromArgb(0xB0, 0x3B, 0x82, 0xF6));
    private static readonly Pen Outline = FrozenPen(Color.FromRgb(0x25, 0x63, 0xEB), 1.5);
    private static readonly Pen Edge = FrozenPen(Color.FromRgb(0x9C, 0xA3, 0xAF), 1);

    private Cell _anchor;
    private bool _dragging;

    public int Columns
    {
        get => (int)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    public int Rows
    {
        get => (int)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public CellSpan Span
    {
        get => (CellSpan)GetValue(SpanProperty);
        set => SetValue(SpanProperty, value);
    }

    public bool IsReadOnly
    {
        get => (bool)GetValue(IsReadOnlyProperty);
        set => SetValue(IsReadOnlyProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (CreateLayout() is not { } layout)
        {
            return;
        }

        var area = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRoundedRectangle(Screen, null, area, 3, 3);

        // Lines closer together than a few pixels would turn the picture grey.
        if (ActualWidth / layout.Columns >= 5)
        {
            for (int c = 1; c < layout.Columns; c++)
            {
                dc.DrawRectangle(Lines, null, new Rect(layout.ColumnEdge(c), 0, 1, ActualHeight));
            }
        }

        if (ActualHeight / layout.Rows >= 5)
        {
            for (int r = 1; r < layout.Rows; r++)
            {
                dc.DrawRectangle(Lines, null, new Rect(0, layout.RowEdge(r), ActualWidth, 1));
            }
        }

        var selected = layout.CellBounds(Span);
        var rect = new Rect(selected.Left, selected.Top, selected.Width, selected.Height);
        if (rect.Width > 2 && rect.Height > 2)
        {
            rect.Inflate(-0.75, -0.75);
        }

        dc.DrawRoundedRectangle(Fill, Outline, rect, 2, 2);
        dc.DrawRoundedRectangle(null, Edge, Rect.Inflate(area, -0.5, -0.5), 3, 3);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        if (IsReadOnly || CreateLayout() is not { } layout)
        {
            return;
        }

        _anchor = layout.CellAt(ToPoint(e.GetPosition(this)));
        _dragging = true;
        CaptureMouse();
        SetCurrentValue(SpanProperty, CellSpan.Single(_anchor));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_dragging && CreateLayout() is { } layout)
        {
            var cell = layout.CellAt(ToPoint(e.GetPosition(this)));
            SetCurrentValue(SpanProperty, CellSpan.FromCorners(_anchor, cell));
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragging = false;
    }

    private GridLayout? CreateLayout()
    {
        int width = (int)ActualWidth;
        int height = (int)ActualHeight;
        return width > 0 && height > 0
            ? new GridLayout(new RectI(0, 0, width, height), Math.Max(1, Columns), Math.Max(1, Rows))
            : null;
    }

    private static PointI ToPoint(Point point) => new((int)point.X, (int)point.Y);

    private static Brush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }
}
