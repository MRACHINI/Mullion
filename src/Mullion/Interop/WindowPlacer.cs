using Mullion.Core;

namespace Mullion.Interop;

/// <summary>Moves and resizes windows that belong to other applications.</summary>
internal static class WindowPlacer
{
    /// <summary>
    /// Puts a window in front of all ordinary windows without giving it the keyboard.
    /// </summary>
    public static void BringToFront(nint window)
    {
        const uint Flags = Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE;

        bool alwaysOnTop = (Native.GetWindowLongPtr(window, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0;
        if (alwaysOnTop)
        {
            Native.SetWindowPos(window, Native.HWND_TOP, 0, 0, 0, 0, Flags);
            return;
        }

        // Simply asking for "top" is often ignored for another program's window.
        // Making it always-on-top for a moment and then ordinary again is not, and
        // leaves it in front of every other ordinary window.
        Native.SetWindowPos(window, Native.HWND_TOPMOST, 0, 0, 0, 0, Flags);
        Native.SetWindowPos(window, Native.HWND_NOTOPMOST, 0, 0, 0, 0, Flags);
    }

    /// <summary>
    /// Moves a window so that its visible bounds match <paramref name="target"/>.
    /// With <paramref name="keepSize"/>, or for a window that cannot be resized,
    /// only the position changes.
    /// </summary>
    public static void Place(nint window, RectI target, bool keepSize)
    {
        if (!Native.IsWindow(window) || target.IsEmpty)
        {
            return;
        }

        // A maximized or minimized window ignores new bounds until it is restored.
        if (Native.IsZoomed(window) || Native.IsIconic(window))
        {
            Native.ShowWindow(window, Native.SW_RESTORE);
        }

        bool resizable = (Native.GetWindowLongPtr(window, Native.GWL_STYLE) & Native.WS_THICKFRAME) != 0;
        bool moveOnly = keepSize || !resizable;

        uint flags = Native.SWP_NOZORDER | Native.SWP_NOACTIVATE | Native.SWP_NOOWNERZORDER;
        if (moveOnly)
        {
            flags |= Native.SWP_NOSIZE;
        }

        // The invisible borders scale with the monitor's DPI, and a window that lands
        // on a monitor with a different scale may resize itself. Repeat until it fits.
        for (int pass = 0; pass < 3; pass++)
        {
            if (!Native.GetWindowRect(window, out var outer))
            {
                return;
            }

            var visible = Native.GetFrameBounds(window) ?? outer.ToRectI();
            int left = visible.Left - outer.Left;
            int top = visible.Top - outer.Top;
            int right = outer.Right - visible.Right;
            int bottom = outer.Bottom - visible.Bottom;

            Native.SetWindowPos(
                window,
                0,
                target.Left - left,
                target.Top - top,
                target.Width + left + right,
                target.Height + top + bottom,
                flags);

            var result = Native.GetFrameBounds(window);
            if (result is not { } now)
            {
                return;
            }

            bool positioned = now.Left == target.Left && now.Top == target.Top;
            bool sized = moveOnly || (now.Width == target.Width && now.Height == target.Height);
            if (positioned && sized)
            {
                return;
            }
        }
    }
}
