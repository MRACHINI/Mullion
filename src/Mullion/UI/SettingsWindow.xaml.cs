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

    private void OnAddRuleClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ZoneChoices.Count == 0)
        {
            MessageBox.Show(
                this,
                "A rule sends windows to a zone, and there are no zones yet.\n\nAdd a zone on the Zones tab first.",
                "Mullion",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var editor = new RuleEditorWindow(
            _viewModel.NewRule(), _viewModel.ZoneChoices, _viewModel.GetOpenWindows(), _viewModel.GetMonitorCount())
        {
            Owner = this,
            Title = "New rule",
        };

        if (editor.ShowDialog() == true)
        {
            _viewModel.AddRule(editor.Rule);
        }
    }

    private void OnEditRuleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleRow row)
        {
            return;
        }

        var editor = new RuleEditorWindow(
            row.Rule, _viewModel.ZoneChoices, _viewModel.GetOpenWindows(), _viewModel.GetMonitorCount())
        {
            Owner = this,
            Title = "Edit rule",
        };

        if (editor.ShowDialog() == true)
        {
            _viewModel.ReplaceRule(row, editor.Rule);
        }
    }

    private void OnDeleteRuleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not RuleRow row)
        {
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Delete the rule for “{row.Program}”?",
            "Mullion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
        {
            _viewModel.RemoveRule(row);
        }
    }

    private void OnApplyRulesClick(object sender, RoutedEventArgs e)
    {
        _viewModel.ApplyRulesNow();
    }

    private void OnForgetRememberedClick(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Forget every placement Mullion remembered by itself?\n\nRules you wrote yourself are kept.",
            "Mullion",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No);

        if (answer == MessageBoxResult.Yes)
        {
            _viewModel.ForgetRemembered();
        }
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
    /// <param name="path">The PNG file to write.</param>
    /// <param name="settings">The settings to show.</param>
    /// <param name="what">
    /// A tab's name (grid, zones, rules, appearance, general), or "zone-editor" or
    /// "rule-editor" for those dialogs, shown for the first zone or rule.
    /// </param>
    public static void RenderPreview(string path, Core.Settings settings, string what)
    {
        var viewModel = new SettingsViewModel(settings);

        if (string.Equals(what, "rule-editor", StringComparison.OrdinalIgnoreCase))
        {
            var rule = settings.Rules.FirstOrDefault() ?? viewModel.NewRule();
            var editor = new RuleEditorWindow(
                rule, settings.Zones, [("notepad++", "notes.txt - Notepad++"), ("chrome", "Mullion - Google Chrome")], 2);
            WindowPicture.Save(editor, path, (int)editor.Width - 16);
            return;
        }

        if (string.Equals(what, "zone-editor", StringComparison.OrdinalIgnoreCase))
        {
            var zone = settings.Zones.FirstOrDefault() ?? viewModel.NewZone();
            var editor = new ZoneEditorWindow(zone, [], _ => { }, _ => false);
            WindowPicture.Save(editor, path, (int)editor.Width - 16);
            return;
        }

        var window = new SettingsWindow(viewModel);
        window.SelectTab(what);
        WindowPicture.Save(window, path, (int)window.Width - 16, (int)window.Height - 39);
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
