using System.Windows;
using System.Windows.Controls;
using Mullion.Core;

namespace Mullion.UI;

/// <summary>The dialog for creating or changing one placement rule.</summary>
internal partial class RuleEditorWindow : Window
{
    private sealed record WindowChoice(string Program, string Title)
    {
        public override string ToString() => Title.Length == 0 ? Program : $"{Program}  —  {Title}";
    }

    private sealed record ZoneChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record MonitorChoice(int Number)
    {
        public override string ToString() => Number == 0 ? "The monitor the window opens on" : $"Monitor {Number}";
    }

    /// <param name="rule">The rule to edit; it is copied, not changed.</param>
    /// <param name="zones">The zones to choose from.</param>
    /// <param name="openWindows">The windows open now, as program and title.</param>
    /// <param name="monitorCount">How many monitors are connected.</param>
    public RuleEditorWindow(
        PlacementRule rule,
        IReadOnlyList<Zone> zones,
        IReadOnlyList<(string Program, string Title)> openWindows,
        int monitorCount)
    {
        InitializeComponent();

        Rule = rule.Clone();
        Rule.Normalize();

        foreach (var (program, title) in openWindows)
        {
            WindowPicker.Items.Add(new WindowChoice(program, title));
        }

        WindowPicker.IsEnabled = WindowPicker.Items.Count > 0;

        // A remembered placement made with the grid gesture has an area of its own
        // rather than a zone; offer to keep it.
        if (Rule.HasArea)
        {
            var area = Rule.AreaAsZone();
            ZonePickerBox.Items.Add(new ZoneChoice(
                string.Empty,
                $"The remembered area ({area.Span.Columns} × {area.Span.Rows} of {area.Columns} × {area.Rows} cells)"));
        }

        foreach (var zone in zones)
        {
            ZonePickerBox.Items.Add(new ZoneChoice(zone.Id, string.IsNullOrWhiteSpace(zone.Name) ? "Unnamed zone" : zone.Name));
        }

        RememberedNote.Visibility = Rule.Learned ? Visibility.Visible : Visibility.Collapsed;

        // Offer every connected monitor, and the rule's own monitor even if it is
        // not connected right now, so opening the editor never changes the rule.
        int lastMonitor = Math.Max(Math.Max(1, monitorCount), Rule.Monitor);
        for (int number = 0; number <= lastMonitor; number++)
        {
            MonitorBox.Items.Add(new MonitorChoice(number));
        }

        // A placement remembered for one window carries that window's whole title;
        // show it as the text to look for, which is what it becomes once saved.
        ProgramBox.Text = Rule.Program;
        TitleBox.Text = Rule.Title.Length > 0 ? Rule.Title : Rule.TitleContains;
        ZonePickerBox.SelectedItem = ZonePickerBox.Items.OfType<ZoneChoice>().FirstOrDefault(choice => choice.Id == Rule.ZoneId);
        MonitorBox.SelectedItem = MonitorBox.Items.OfType<MonitorChoice>().First(choice => choice.Number == Rule.Monitor);

        UpdateSaveButton();
        Loaded += (_, _) => ProgramBox.Focus();
    }

    /// <summary>The edited rule. Valid after the dialog was closed with Save.</summary>
    public PlacementRule Rule { get; }

    private void OnWindowPicked(object sender, SelectionChangedEventArgs e)
    {
        if (WindowPicker.SelectedItem is WindowChoice choice)
        {
            // Only the program is filled in: titles change from document to document,
            // so a rule that copied the whole title would rarely match again.
            ProgramBox.Text = choice.Program;
        }
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e) => UpdateSaveButton();

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSaveButton();

    private void UpdateSaveButton()
    {
        // A rule needs a program to recognise and a zone to send it to.
        SaveButton.IsEnabled =
            PlacementRules.NormalizeProgram(ProgramBox.Text).Length > 0 && ZonePickerBox.SelectedItem is ZoneChoice;
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        if (ZonePickerBox.SelectedItem is not ZoneChoice zone)
        {
            return;
        }

        Rule.Program = ProgramBox.Text;
        Rule.TitleContains = TitleBox.Text;
        Rule.Monitor = (MonitorBox.SelectedItem as MonitorChoice)?.Number ?? 0;

        // The empty choice stands for the rule's own area, which is then kept as it is.
        if (zone.Id.Length > 0)
        {
            Rule.SetZone(zone.Id);
        }

        // Once saved from here it is the user's own rule: Mullion no longer updates it,
        // and it matches on the text in the title box rather than on one exact title.
        Rule.Learned = false;
        Rule.Title = string.Empty;
        Rule.Normalize();

        DialogResult = true;
    }
}
