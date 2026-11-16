using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Mullion.UI;

/// <summary>
/// Draws one of Mullion's windows into a PNG file without ever showing it. Used for
/// documentation pictures and for checking a window's look without capturing the
/// screen, which could include whatever else is on it.
/// </summary>
internal static class WindowPicture
{
    /// <param name="window">A window that has been created but not shown.</param>
    /// <param name="path">The PNG file to write.</param>
    /// <param name="width">Width of the window's contents.</param>
    /// <param name="height">Height of the contents, or null to use the height the contents ask for.</param>
    public static void Save(Window window, string path, int width, int? height = null)
    {
        // Take the content out of the window so it can be laid out at a chosen size.
        var root = (FrameworkElement)window.Content;
        object? dataContext = window.DataContext;
        window.Content = null;
        root.DataContext = dataContext;

        root.Measure(new Size(width, height ?? double.PositiveInfinity));
        int finalHeight = height ?? (int)Math.Ceiling(root.DesiredSize.Height);
        root.Arrange(new Rect(0, 0, width, finalHeight));

        // Let the bindings and list items catch up, then lay out again.
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.Loaded);
        root.Measure(new Size(width, height ?? double.PositiveInfinity));
        finalHeight = height ?? (int)Math.Ceiling(root.DesiredSize.Height);
        root.Arrange(new Rect(0, 0, width, finalHeight));
        root.UpdateLayout();

        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
        {
            dc.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, width, finalHeight));
        }

        var bitmap = new RenderTargetBitmap(width, finalHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(background);
        bitmap.Render(root);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
