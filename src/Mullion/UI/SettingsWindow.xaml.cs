using System.Windows;

namespace Mullion.UI;

/// <summary>The settings window. Changes apply as they are made; there is nothing to confirm.</summary>
internal partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow(SettingsViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // While the box waits for keys, record system-wide so that combinations
        // another program owns can be seen too.
        ArrangeBox.GotKeyboardFocus += (_, _) => _viewModel.SetRecorder(ArrangeBox.Accept);
        ArrangeBox.LostKeyboardFocus += (_, _) => _viewModel.SetRecorder(null);
    }

    private void OnClearArrangeClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ArrangeHotkey = null;
    }

    private void OnStartArrangeClick(object sender, RoutedEventArgs e)
    {
        _viewModel.StartArranging();
    }

    /// <summary>
    /// Draws one tab of the settings window into a PNG file, without showing
    /// anything. Used for documentation pictures and for checking the look.
    /// </summary>
    public static void RenderPreview(string path, Core.Settings settings, string tab)
    {
        var viewModel = new SettingsViewModel(settings);
        var window = new SettingsWindow(viewModel);
        window.SelectTab(tab);

        // Take the content out of the window so it can be laid out without being shown.
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        root.DataContext = viewModel;

        int width = (int)window.Width - 16;
        int height = (int)window.Height - 39;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));

        // Let the bindings and the list items catch up, then lay out again.
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Loaded);
        root.UpdateLayout();

        var background = new System.Windows.Media.DrawingVisual();
        using (var dc = background.RenderOpen())
        {
            dc.DrawRectangle(window.Background, null, new Rect(0, 0, width, height));
        }

        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
            width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(background);
        bitmap.Render(root);

        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = System.IO.File.Create(path);
        encoder.Save(file);
    }

    /// <summary>Shows the tab with the given header, such as "Zones".</summary>
    public void SelectTab(string header)
    {
        foreach (var item in Tabs.Items)
        {
            if (item is System.Windows.Controls.TabItem tab
                && string.Equals(tab.Header as string, header, StringComparison.OrdinalIgnoreCase))
            {
                Tabs.SelectedItem = tab;
                return;
            }
        }
    }

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetToDefaults();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnAddZoneClick(object sender, RoutedEventArgs e)
    {
        var editor = new ZoneEditorWindow(
            _viewModel.NewZone(), _viewModel.ZonesExcept(null), _viewModel.SetRecorder, _viewModel.IsOwnedElsewhere)
        {
            Owner = this,
            Title = "New zone",
        };

        if (editor.ShowDialog() == true)
        {
            _viewModel.AddZone(editor.Zone);
        }
    }

    private void OnEditZoneClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ZoneRow row)
        {
            return;
        }

        var editor = new ZoneEditorWindow(
            row.Zone, _viewModel.ZonesExcept(row), _viewModel.SetRecorder, _viewModel.IsOwnedElsewhere)
        {
            Owner = this,
            Title = "Edit zone",
        };

        if (editor.ShowDialog() == true)
        {
            _viewModel.ReplaceZone(row, editor.Zone);
        }
    }

    private void OnDeleteZoneClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ZoneRow row)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Delete the zone “{row.Name}”?",
            "Mullion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
        {
            _viewModel.RemoveZone(row);
        }
    }
}
