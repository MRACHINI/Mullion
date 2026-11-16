using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Mullion.Core;

namespace Mullion.UI;

/// <summary>One zone as drawn on a monitor while arranging windows.</summary>
internal sealed record ArrangeZone(RectI Bounds, string Name, string Shortcut);

/// <summary>
/// Draws the zones of one monitor while windows are being arranged, highlights the
/// zone under the mouse and the window currently being placed, and reports clicks.
/// </summary>
internal sealed class ArrangeSurface : FrameworkElement
{
    private static readonly Typeface NameTypeface = new("Segoe UI Semibold");
    private static readonly Typeface KeyTypeface = new("Segoe UI");
    private static readonly Brush Solid = MakeBrush("#000000", 1.0 / 255);
    private static readonly Brush ZoneFill = MakeBrush("#FFFFFF", 0.07);
    private static readonly Brush LabelBackground = MakeBrush("#000000", 0.62);
    private static readonly Brush KeyText = MakeBrush("#FFFFFF", 0.75);
    private static readonly Pen ZonePen = MakePen("#FFFFFF", 0.8, 1.5);
    private static readonly Pen ZoneShadowPen = MakePen("#000000", 0.35, 4);
    private static readonly Pen CurrentPen = MakePen("#FBBF24", 1, 5);
    private static readonly Brush TagBackground = MakeBrush("#FBBF24", 1);
    private static readonly Brush TagText = MakeBrush("#1F2937", 1);

    /// <summary>How long the highlight flashes when another window becomes the current one.</summary>
    private const int FlashSteps = 16;

    private readonly System.Windows.Threading.DispatcherTimer _flashTimer;
    private int _flashStep = FlashSteps;

    private RectI _area;
    private IReadOnlyList<ArrangeZone> _zones = [];
    private RectI? _current;
    private int _hover = -1;
    private Brush _tint = MakeBrush("#000000", 0.6);
    private Brush _hoverFill = MakeBrush("#3B82F6", 0.45);
    private Pen _hoverPen = MakePen("#3B82F6", 1, 3);

    /// <summary>Raised with the position of the zone that was clicked.</summary>
    public event Action<int>? ZoneChosen;

    /// <summary>Raised on a right-click, which skips the current window.</summary>
    public event Action? SkipRequested;

    public ArrangeSurface()
    {
        _flashTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
        _flashTimer.Tick += (_, _) =>
        {
            _flashStep++;
            if (_flashStep >= FlashSteps)
            {
                _flashTimer.Stop();
            }

            InvalidateVisual();
        };
        Unloaded += (_, _) => _flashTimer.Stop();
    }

    public void Configure(RectI area, IReadOnlyList<ArrangeZone> zones, AppearanceSettings appearance)
    {
        _area = area;
        _zones = zones;

        // Everything except the window being placed is darkened well beyond the
        // usual tint, so that window is the one bright thing on the screen.
        _tint = MakeBrush(appearance.TintColor, Math.Max(appearance.TintOpacity, 0.6));
        _hoverFill = MakeBrush(appearance.SelectionColor, Math.Max(appearance.SelectionOpacity, 0.35));
        _hoverPen = MakePen(appearance.SelectionColor, 1, 3);
        InvalidateVisual();
    }

    /// <summary>Sets the visible bounds of the window being placed, in screen pixels; null for none.</summary>
    public void SetCurrentWindow(RectI? frame)
    {
        _current = frame;

        // Flash the highlight for a moment so the eye is drawn to the new window,
        // but only on the monitor that actually shows it.
        bool visibleHere = frame is { } f
            && f.Left < _area.Right && f.Right > _area.Left && f.Top < _area.Bottom && f.Bottom > _area.Top;
        if (visibleHere)
        {
            _flashStep = 0;
            _flashTimer.Start();
        }
        else
        {
            _flashStep = FlashSteps;
            _flashTimer.Stop();
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var full = new Rect(0, 0, ActualWidth, ActualHeight);
        if (_area.IsEmpty || full.IsEmpty)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = 1 / dpi.DpiScaleX;
        double sy = 1 / dpi.DpiScaleY;

        Rect ToLocal(RectI r) => new(
            (r.Left - _area.Left) * sx,
            (r.Top - _area.Top) * sy,
            Math.Max(0, r.Width * sx),
            Math.Max(0, r.Height * sy));

        // Every pixel gets at least a trace of colour so that clicks always land here.
        dc.DrawRectangle(Solid, null, full);

        // Dim everything except the window being placed, so it stands out.
        Geometry dimmed = new RectangleGeometry(full);
        Rect? currentRect = null;
        if (_current is { } current)
        {
            var hole = Rect.Intersect(ToLocal(current), full);
            if (!hole.IsEmpty && hole.Width > 4 && hole.Height > 4)
            {
                currentRect = hole;
                dimmed = new CombinedGeometry(GeometryCombineMode.Exclude, dimmed, new RectangleGeometry(hole));
            }
        }

        dc.DrawGeometry(_tint, null, dimmed);

        for (int i = 0; i < _zones.Count; i++)
        {
            if (i != _hover)
            {
                DrawZone(dc, ToLocal(_zones[i].Bounds), ZoneFill, ZonePen);
            }
        }

        if (_hover >= 0 && _hover < _zones.Count)
        {
            DrawZone(dc, ToLocal(_zones[_hover].Bounds), _hoverFill, _hoverPen);
        }

        for (int i = 0; i < _zones.Count; i++)
        {
            DrawLabel(dc, _zones[i], ToLocal(_zones[i].Bounds), dpi.PixelsPerDip);
        }

        // Drawn last so nothing covers the marks on the window being placed.
        if (currentRect is { } outline)
        {
            DrawCurrentWindow(dc, outline, full, dpi.PixelsPerDip);
        }
    }

    /// <summary>A thick border with a glow around the window being placed, and a tag on its corner.</summary>
    private void DrawCurrentWindow(DrawingContext dc, Rect outline, Rect full, double pixelsPerDip)
    {
        // The flash swells the glow twice, then settles.
        double progress = Math.Min(1.0, (double)_flashStep / FlashSteps);
        double swell = progress >= 1 ? 0 : Math.Abs(Math.Sin(progress * Math.PI * 2)) * (1 - progress);

        for (int ring = 3; ring >= 1; ring--)
        {
            double spread = (ring * 4) + (swell * ring * 10);
            var glow = new Pen(MakeBrush("#FBBF24", 0.28 - (ring * 0.06) + (swell * 0.2)), 8 + (swell * 6));
            dc.DrawRoundedRectangle(null, glow, Rect.Inflate(outline, spread, spread), 6, 6);
        }

        dc.DrawRectangle(null, CurrentPen, Rect.Inflate(outline, -2.5, -2.5));

        var text = new FormattedText(
            "PLACING THIS WINDOW",
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            NameTypeface,
            12,
            TagText,
            pixelsPerDip);

        double width = text.Width + 20;
        double height = text.Height + 8;
        if (outline.Width < width + 16 || outline.Height < height + 16)
        {
            return;
        }

        // On the window's top-left corner; kept on screen for a window that
        // reaches past the edge of the monitor.
        var tag = new Rect(
            Math.Clamp(outline.Left + 5, full.Left, Math.Max(full.Left, full.Right - width)),
            Math.Clamp(outline.Top + 5, full.Top, Math.Max(full.Top, full.Bottom - height)),
            width,
            height);

        dc.DrawRoundedRectangle(TagBackground, null, tag, 4, 4);
        dc.DrawText(text, new Point(tag.Left + 10, tag.Top + 4));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        SetHover(ZoneAt(e.GetPosition(this)));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        SetHover(-1);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        int zone = ZoneAt(e.GetPosition(this));
        Log.Trace($"arrange: click at {e.GetPosition(this)} on the monitor at {_area.Left},{_area.Top} is zone {zone}");
        if (zone >= 0)
        {
            e.Handled = true;
            ZoneChosen?.Invoke(zone);
        }
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        e.Handled = true;
        SkipRequested?.Invoke();
    }

    private int ZoneAt(Point position)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var point = new PointI(
            _area.Left + (int)(position.X * dpi.DpiScaleX),
            _area.Top + (int)(position.Y * dpi.DpiScaleY));

        var bounds = new RectI[_zones.Count];
        for (int i = 0; i < bounds.Length; i++)
        {
            bounds[i] = _zones[i].Bounds;
        }

        return ZoneHitTest.Pick(bounds, point);
    }

    private void SetHover(int zone)
    {
        if (zone != _hover)
        {
            _hover = zone;
            InvalidateVisual();
        }
    }

    private static void DrawZone(DrawingContext dc, Rect rect, Brush fill, Pen pen)
    {
        if (rect.Width > 6 && rect.Height > 6)
        {
            rect.Inflate(-2, -2);
        }

        // A dark line under the light one keeps the outline visible over a bright
        // window as well as over the darkened background.
        dc.DrawRoundedRectangle(null, ZoneShadowPen, rect, 6, 6);
        dc.DrawRoundedRectangle(fill, pen, rect, 6, 6);
    }

    private static void DrawLabel(DrawingContext dc, ArrangeZone zone, Rect rect, double pixelsPerDip)
    {
        string name = string.IsNullOrWhiteSpace(zone.Name) ? "Zone" : zone.Name;
        var title = new FormattedText(
            name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, NameTypeface, 15, Brushes.White, pixelsPerDip)
        {
            MaxTextWidth = Math.Max(40, rect.Width - 40),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
        };

        FormattedText? key = zone.Shortcut.Length == 0
            ? null
            : new FormattedText(
                zone.Shortcut, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, KeyTypeface, 12, KeyText, pixelsPerDip);

        double width = Math.Max(title.Width, key?.Width ?? 0) + 24;
        double height = title.Height + (key?.Height ?? 0) + 12;
        if (width > rect.Width - 8 || height > rect.Height - 8)
        {
            return;
        }

        var pill = new Rect(
            rect.Left + ((rect.Width - width) / 2),
            rect.Top + ((rect.Height - height) / 2),
            width,
            height);

        dc.DrawRoundedRectangle(LabelBackground, null, pill, 8, 8);
        dc.DrawText(title, new Point(pill.Left + ((width - title.Width) / 2), pill.Top + 6));
        if (key is not null)
        {
            dc.DrawText(key, new Point(pill.Left + ((width - key.Width) / 2), pill.Top + 6 + title.Height));
        }
    }

    private static Pen MakePen(string hex, double opacity, double thickness)
    {
        var pen = new Pen(MakeBrush(hex, opacity), thickness);
        pen.Freeze();
        return pen;
    }

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
