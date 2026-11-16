using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Mullion.Core;
using Mullion.Interop;

namespace Mullion.UI;

/// <summary>
/// Covers one monitor while windows are being arranged: shows the zones to click
/// and a banner naming the window that is being placed.
/// </summary>
internal partial class ArrangeOverlay : Window
{
    private readonly RectI _area;
    private nint _handle;

    public ArrangeOverlay(RectI area, IReadOnlyList<ArrangeZone> zones, AppearanceSettings appearance)
    {
        _area = area;
        InitializeComponent();

        Surface.Configure(area, zones, appearance);
        Surface.ZoneChosen += zone => ZoneChosen?.Invoke(zone);
        Surface.SkipRequested += () => SkipRequested?.Invoke();
    }

    /// <summary>Raised with the position of the zone clicked on this monitor.</summary>
    public event Action<int>? ZoneChosen;

    public event Action? SkipRequested;
    public event Action? BackRequested;
    public event Action? DoneRequested;

    /// <summary>Shows the overlay over its monitor.</summary>
    public void ShowOverMonitor()
    {
        _handle = new WindowInteropHelper(this).EnsureHandle();
        long style = Native.GetWindowLongPtr(_handle, Native.GWL_EXSTYLE);
        Native.SetWindowLongPtr(_handle, Native.GWL_EXSTYLE, (nint)(style | Native.WS_EX_TOOLWINDOW));

        // Twice on purpose: crossing to a monitor with a different scale makes
        // Windows rescale the window after the first call.
        for (int i = 0; i < 2; i++)
        {
            Native.SetWindowPos(
                _handle, Native.HWND_TOPMOST, _area.Left, _area.Top, _area.Width, _area.Height, Native.SWP_NOACTIVATE);
        }

        Show();
    }

    /// <summary>
    /// Puts the overlay back above every other window. Needed after the window being
    /// placed was brought forward, in case that window is an always-on-top one.
    /// </summary>
    public void KeepOnTop()
    {
        if (_handle != 0)
        {
            Native.SetWindowPos(
                _handle,
                Native.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// Draws the overlay for a sample screen into a PNG file, without showing
    /// anything. Used for documentation pictures and for checking the look.
    /// </summary>
    public static void RenderPreview(string path, Settings settings, int width = 1280, int height = 720)
    {
        var area = new RectI(0, 0, width, height);
        var sample = settings.Zones.Count > 0
            ? settings.Zones
            :
            [
                new Zone { Name = "Left half", Columns = 2, Rows = 1, Shortcut = "Ctrl+Alt+1" },
                new Zone { Name = "Top right", Columns = 2, Rows = 2, Column0 = 1, Column1 = 1, Shortcut = "Ctrl+Alt+2" },
                new Zone { Name = "Bottom right", Columns = 2, Rows = 2, Column0 = 1, Column1 = 1, Row0 = 1, Row1 = 1 },
            ];

        var zones = sample
            .Select(zone => new ArrangeZone(
                zone.BoundsOn(area, settings.WindowGap, settings.EdgeGap), zone.Name, zone.Shortcut))
            .ToList();

        var current = new RectI(width * 22 / 100, height * 30 / 100, width * 62 / 100, height * 84 / 100);
        var overlay = new ArrangeOverlay(area, zones, settings.Appearance);
        overlay.SetCurrent(current, "Quarterly report.docx - Word", 2, 7);

        // Take the content out of the window so it can be laid out at a chosen size.
        var root = (FrameworkElement)overlay.Content;
        overlay.Content = null;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();

        // A plain stand-in for the desktop and the window being placed.
        var desktop = new System.Windows.Media.DrawingVisual();
        using (var dc = desktop.RenderOpen())
        {
            var gray = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCB, 0xD5, 0xE1));
            dc.DrawRectangle(gray, null, new Rect(0, 0, width, height));
            dc.DrawRectangle(
                System.Windows.Media.Brushes.White, null, new Rect(current.Left, current.Top, current.Width, current.Height));
            dc.DrawRectangle(
                new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x25, 0x63, 0xEB)),
                null,
                new Rect(current.Left, current.Top, current.Width, 28));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(desktop);
        bitmap.Render(root);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Updates the banner and the highlight for the window now being placed.</summary>
    /// <param name="frame">Its visible bounds in screen pixels, or null when unknown.</param>
    /// <param name="title">Its title.</param>
    /// <param name="number">Its position among the windows, starting at 1.</param>
    /// <param name="count">How many windows there are.</param>
    public void SetCurrent(RectI? frame, string title, int number, int count)
    {
        CounterText.Text = $"WINDOW {number} OF {count}";
        TitleText.Text = string.IsNullOrWhiteSpace(title) ? "(untitled window)" : title;
        BackButton.IsEnabled = number > 1;
        Surface.SetCurrentWindow(frame);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                e.Handled = true;
                DoneRequested?.Invoke();
                break;

            case Key.Space:
            case Key.Right:
            case Key.Enter:
                e.Handled = true;
                SkipRequested?.Invoke();
                break;

            case Key.Left:
            case Key.Back:
                e.Handled = true;
                BackRequested?.Invoke();
                break;
        }

        base.OnPreviewKeyDown(e);
    }

    private void OnBackClick(object sender, RoutedEventArgs e) => BackRequested?.Invoke();

    private void OnSkipClick(object sender, RoutedEventArgs e) => SkipRequested?.Invoke();

    private void OnDoneClick(object sender, RoutedEventArgs e) => DoneRequested?.Invoke();
}
