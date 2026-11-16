using System.Windows;
using Forms = System.Windows.Forms;

namespace Mullion.UI;

/// <summary>The notification-area icon and its menu.</summary>
internal sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    private readonly Forms.ToolStripMenuItem _pauseItem;
    private readonly System.Drawing.Icon? _image;

    public TrayIcon(Action openSettings, Action arrangeWindows, Action<bool> setPaused, Action exit)
    {
        _image = LoadIcon();

        _pauseItem = new Forms.ToolStripMenuItem("Pause") { CheckOnClick = true };
        _pauseItem.CheckedChanged += (_, _) =>
        {
            setPaused(_pauseItem.Checked);
            UpdateText();
        };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(new Forms.ToolStripMenuItem("Arrange windows…", null, (_, _) => arrangeWindows()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Settings…", null, (_, _) => openSettings()));
        menu.Items.Add(_pauseItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem("Exit", null, (_, _) => exit()));

        _icon = new Forms.NotifyIcon
        {
            Icon = _image ?? System.Drawing.SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.DoubleClick += (_, _) => openSettings();
        UpdateText();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image?.Dispose();
    }

    private void UpdateText()
    {
        _icon.Text = _pauseItem.Checked ? "Mullion (paused)" : "Mullion";
    }

    private static System.Drawing.Icon? LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/mullion.ico"));
            if (resource is null)
            {
                return null;
            }

            using var stream = resource.Stream;
            return new System.Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);
        }
        catch (Exception ex)
        {
            Log.Write("Could not load the tray icon.", ex);
            return null;
        }
    }
}
