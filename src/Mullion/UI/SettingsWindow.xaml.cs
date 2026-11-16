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

    private void OnResetClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ResetToDefaults();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
