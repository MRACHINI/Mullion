using System.Globalization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Mullion.Core;
using Mullion.Interop;

namespace Mullion.UI;

/// <summary>
/// The translucent grid drawn over a monitor while a gesture is in progress.
/// It never takes keyboard focus and does not appear in the taskbar or Alt+Tab.
/// </summary>
internal sealed class OverlayWindow : Window
{
    private readonly GridSurface _surface = new();
    private nint _handle;
    private RectI _bounds;

    public OverlayWindow()
    {
        Title = "Mullion grid";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = 0;
        Top = 0;
        Width = 200;
        Height = 200;
        Content = _surface;
    }

    /// <summary>Creates the window without showing it, so the first gesture has nothing to wait for.</summary>
    public void Prepare()
    {
        _handle = new WindowInteropHelper(this).EnsureHandle();
        long style = Native.GetWindowLongPtr(_handle, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(
            _handle, Native.GWL_EXSTYLE, (nint)(style | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE));
    }

    public void SetAppearance(AppearanceSettings appearance)
    {
        _surface.SetAppearance(appearance);
    }

    /// <summary>Shows the given state, or hides the overlay when it is null.</summary>
    public void Apply(OverlayModel? model)
    {
        if (model is null)
        {
            if (IsVisible)
            {
                Hide();
            }

            return;
        }

        if (_handle == 0)
        {
            Prepare();
        }

        if (model.Area != _bounds || !IsVisible)
        {
            MoveTo(model.Area);
        }

        _surface.SetModel(model);

        if (!IsVisible)
        {
            Show();
        }
    }

    /// <summary>
    /// Draws the overlay for a sample selection into a PNG file, without showing
    /// anything on screen. Used for documentation pictures and for checking the look.
    /// </summary>
    public static void RenderPreview(string path, Settings settings, int width = 1280, int height = 720)
    {
        var area = new RectI(0, 0, width, height);
        var layout = new GridLayout(area, settings.Columns, settings.Rows, settings.WindowGap, settings.EdgeGap);
        var span = CellSpan.FromCorners(
            layout.CellAt(new PointI(width * 3 / 8, height * 3 / 8)),
            layout.CellAt(new PointI(width * 5 / 8, height * 5 / 8)));
        var model = new OverlayModel(area, layout.Columns, layout.Rows, layout.WindowBounds(span), span, OverlayKind.Select);

        var surface = new GridSurface();
        surface.SetAppearance(settings.Appearance);
        surface.SetModel(model);
        surface.Measure(new Size(width, height));
        surface.Arrange(new Rect(0, 0, width, height));

        // A plain stand-in for the desktop, so the tint and lines can be judged.
        var desktop = new DrawingVisual();
        using (var dc = desktop.RenderOpen())
        {
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)), null, new Rect(0, 0, width, height));
            dc.DrawRectangle(Brushes.White, null, new Rect(width * 0.08, height * 0.12, width * 0.5, height * 0.6));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x1F, 0x29, 0x37)), null, new Rect(width * 0.55, height * 0.3, width * 0.38, height * 0.6));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(desktop);
        bitmap.Render(surface);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    private void MoveTo(RectI area)
    {
        _bounds = area;

        // Twice on purpose: when the window crosses to a monitor with a different
        // scale, Windows rescales it after the first call.
        for (int i = 0; i < 2; i++)
        {
            Native.SetWindowPos(
                _handle, Native.HWND_TOPMOST, area.Left, area.Top, area.Width, area.Height, Native.SWP_NOACTIVATE);
        }
    }

    /// <summary>Draws the tint, the grid lines, the selection and its size label.</summary>
    private sealed class GridSurface : FrameworkElement
    {
        private static readonly Typeface LabelTypeface = new("Segoe UI Semibold");

        private OverlayModel? _model;
        private Brush _tint = Brushes.Transparent;
        private Brush _line = Brushes.White;
        private Brush _fill = Brushes.Transparent;
        private Pen _border = new(Brushes.White, 2);
        private bool _showLabel = true;

        public void SetModel(OverlayModel model)
        {
            _model = model;
            InvalidateVisual();
        }

        public void SetAppearance(AppearanceSettings appearance)
        {
            // An alpha of at least 1 keeps every pixel "solid" for Windows, so mouse
            // events over the grid go to the overlay and not to the window beneath.
            _tint = MakeBrush(appearance.TintColor, Math.Max(appearance.TintOpacity, 1.0 / 255));
            _line = MakeBrush(appearance.LineColor, appearance.LineOpacity);
            _fill = MakeBrush(appearance.SelectionColor, appearance.SelectionOpacity);
            _border = new Pen(MakeBrush(appearance.SelectionColor, Math.Min(1, appearance.SelectionOpacity + 0.5)), 2);
            _border.Freeze();
            _showLabel = appearance.ShowSizeLabel;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            dc.DrawRectangle(_tint, null, new Rect(0, 0, ActualWidth, ActualHeight));

            if (_model is not { } model)
            {
                return;
            }

            // The model is in screen pixels; WPF draws in units that are scaled by the
            // monitor's DPI. Dividing by the scale lands every line on a real pixel.
            var dpi = VisualTreeHelper.GetDpi(this);
            double sx = 1 / dpi.DpiScaleX;
            double sy = 1 / dpi.DpiScaleY;
            var area = model.Area;
            var layout = new GridLayout(area, model.Columns, model.Rows);

            for (int c = 1; c < layout.Columns; c++)
            {
                double x = (layout.ColumnEdge(c) - area.Left) * sx;
                dc.DrawRectangle(_line, null, new Rect(x, 0, sx, area.Height * sy));
            }

            for (int r = 1; r < layout.Rows; r++)
            {
                double y = (layout.RowEdge(r) - area.Top) * sy;
                dc.DrawRectangle(_line, null, new Rect(0, y, area.Width * sx, sy));
            }

            var highlight = model.Highlight;
            var rect = new Rect(
                (highlight.Left - area.Left) * sx,
                (highlight.Top - area.Top) * sy,
                Math.Max(0, highlight.Width * sx),
                Math.Max(0, highlight.Height * sy));

            if (rect.Width < 3 || rect.Height < 3)
            {
                return;
            }

            var inner = Rect.Inflate(rect, -1, -1);
            dc.DrawRoundedRectangle(_fill, _border, inner, 4, 4);

            if (_showLabel)
            {
                DrawLabel(dc, model, rect, dpi.PixelsPerDip);
            }
        }

        private static void DrawLabel(DrawingContext dc, OverlayModel model, Rect rect, double pixelsPerDip)
        {
            string size = $"{model.Highlight.Width} × {model.Highlight.Height}";
            string text = model.Span is { } span ? $"{span.Columns} × {span.Rows}   ·   {size}" : size;

            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                LabelTypeface,
                15,
                Brushes.White,
                pixelsPerDip);

            double width = formatted.Width + 24;
            double height = formatted.Height + 10;
            if (width > rect.Width - 8 || height > rect.Height - 8)
            {
                return;
            }

            var pill = new Rect(
                rect.Left + ((rect.Width - width) / 2),
                rect.Top + ((rect.Height - height) / 2),
                width,
                height);

            dc.DrawRoundedRectangle(LabelBackground, null, pill, height / 2, height / 2);
            dc.DrawText(formatted, new Point(pill.Left + 12, pill.Top + 5));
        }

        private static readonly Brush LabelBackground = MakeBrush("#000000", 0.6);

        private static Brush MakeBrush(string hex, double opacity)
        {
            Color color;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(hex);
            }
            catch (FormatException)
            {
                color = Colors.White;
            }

            color.A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }
    }
}
