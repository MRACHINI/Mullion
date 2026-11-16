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
        var editor = new ZoneEditorWindow(_viewModel.NewZone(), _viewModel.ZonesExcept(null), _viewModel.SetRecording)
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

        var editor = new ZoneEditorWindow(row.Zone, _viewModel.ZonesExcept(row), _viewModel.SetRecording)
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
