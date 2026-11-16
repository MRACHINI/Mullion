using Mullion.Core;

namespace Mullion.Core.Tests;

public sealed class PlacementRuleTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mullion-rules-" + Guid.NewGuid().ToString("N"));

    public PlacementRuleTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    private static PlacementRule Rule(string program, string title = "", bool enabled = true) =>
        new() { Program = program, TitleContains = title, ZoneId = "z", Enabled = enabled };

    [Theory]
    [InlineData("notepad++", "notepad++", true)]
    [InlineData("notepad++", "Notepad++", true)]
    [InlineData("notepad++", "NOTEPAD++.EXE", true)]
    [InlineData("notepad++", " notepad++.exe ", true)]
    [InlineData("notepad++", "notepad", false)]
    [InlineData("notepad++", "", false)]
    public void Program_is_matched_whatever_its_case_and_with_or_without_exe(string rule, string program, bool expected)
    {
        Assert.Equal(expected, Rule(rule).Matches(program, "anything"));
    }

    [Theory]
    [InlineData("", "Budget.xlsx - Excel", true)]
    [InlineData("budget", "Budget.xlsx - Excel", true)]
    [InlineData("EXCEL", "Budget.xlsx - Excel", true)]
    [InlineData("report", "Budget.xlsx - Excel", false)]
    [InlineData("budget", "", false)]
    public void Title_text_is_optional_and_matched_whatever_its_case(string titleContains, string title, bool expected)
    {
        Assert.Equal(expected, Rule("excel", titleContains).Matches("EXCEL", title));
    }

    [Fact]
    public void A_rule_that_is_switched_off_or_has_no_program_matches_nothing()
    {
        Assert.False(Rule("excel", enabled: false).Matches("excel", "x"));
        Assert.False(Rule("").Matches("", "x"));
    }

    [Fact]
    public void Program_names_are_tidied()
    {
        var rule = new PlacementRule { Program = "  Notepad++.EXE ", TitleContains = "  draft  ", Monitor = 99 };

        rule.Normalize();

        Assert.Equal("Notepad++", rule.Program);
        Assert.Equal("draft", rule.TitleContains);
        Assert.Equal(PlacementRule.MaxMonitor, rule.Monitor);
    }

    [Fact]
    public void The_rule_asking_for_the_most_title_text_wins()
    {
        var general = Rule("excel");
        var budget = Rule("excel", "budget");
        var budget2026 = Rule("excel", "budget 2026");
        PlacementRule[] rules = [general, budget, budget2026];

        Assert.Same(general, PlacementRules.Find(rules, "excel", "Report.xlsx - Excel"));
        Assert.Same(budget, PlacementRules.Find(rules, "excel", "Budget 2025.xlsx - Excel"));
        Assert.Same(budget2026, PlacementRules.Find(rules, "excel", "Budget 2026.xlsx - Excel"));
        Assert.Null(PlacementRules.Find(rules, "winword", "Budget 2026.docx - Word"));
    }

    [Fact]
    public void Between_equally_specific_rules_the_earlier_one_wins()
    {
        var first = Rule("excel");
        var second = Rule("excel");

        Assert.Same(first, PlacementRules.Find([first, second], "excel", "x"));
    }

    [Fact]
    public void Switched_off_rules_are_passed_over()
    {
        var off = Rule("excel", "budget", enabled: false);
        var general = Rule("excel");

        Assert.Same(general, PlacementRules.Find([off, general], "excel", "Budget.xlsx"));
    }

    private static PlacementRule ZonePlacement(string zoneId, int monitor = 1) =>
        new() { ZoneId = zoneId, Monitor = monitor };

    private static PlacementRule AreaPlacement(int columns, int rows, CellSpan span, int monitor = 1)
    {
        var placement = new PlacementRule { Monitor = monitor };
        placement.SetArea(columns, rows, span);
        return placement;
    }

    [Fact]
    public void A_written_rule_beats_a_remembered_placement()
    {
        var remembered = new PlacementRule { Program = "excel", TitleContains = "budget 2026", ZoneId = "a", Learned = true };
        var written = Rule("excel");

        Assert.Same(written, PlacementRules.Find([remembered, written], "excel", "Budget 2026.xlsx"));
        Assert.Same(remembered, PlacementRules.Find([remembered], "excel", "Budget 2026.xlsx"));
    }

    [Fact]
    public void Placing_a_window_is_remembered_for_its_title_and_for_its_program()
    {
        var rules = new List<PlacementRule>();

        Assert.True(PlacementRules.Remember(rules, "Notepad++.exe", " notes.txt ", ZonePlacement("right", monitor: 2)));

        Assert.Equal(2, rules.Count);

        var forTitle = Assert.Single(rules, rule => rule.Title.Length > 0);
        Assert.Equal("notes.txt", forTitle.Title);
        Assert.True(forTitle.Matches("notepad++", "NOTES.TXT"));
        Assert.False(forTitle.Matches("notepad++", "notes.txt (2)"));

        var forProgram = Assert.Single(rules, rule => rule.Title.Length == 0);
        Assert.True(forProgram.Matches("notepad++", "anything else"));

        Assert.All(rules, rule =>
        {
            Assert.True(rule.Learned);
            Assert.True(rule.Enabled);
            Assert.Equal("Notepad++", rule.Program);
            Assert.Equal(string.Empty, rule.TitleContains);
            Assert.Equal("right", rule.ZoneId);
            Assert.Equal(2, rule.Monitor);
        });
    }

    [Fact]
    public void A_window_placed_before_returns_to_its_own_spot_and_a_new_one_goes_where_the_last_was_put()
    {
        var rules = new List<PlacementRule>();
        PlacementRules.Remember(rules, "claude", "Project A", ZonePlacement("left", monitor: 1));
        PlacementRules.Remember(rules, "claude", "Project B", ZonePlacement("right", monitor: 2));

        // Each known window has its own place.
        Assert.Equal("left", PlacementRules.Find(rules, "claude", "Project A")!.ZoneId);
        Assert.Equal("right", PlacementRules.Find(rules, "claude", "Project B")!.ZoneId);

        // A window never seen before goes where the program's last window was put.
        var fallback = PlacementRules.Find(rules, "claude", "Brand new chat")!;
        Assert.Equal("right", fallback.ZoneId);
        Assert.Equal(2, fallback.Monitor);
        Assert.Equal(string.Empty, fallback.Title);

        // Another program is not affected.
        Assert.Null(PlacementRules.Find(rules, "chrome", "Project A"));
    }

    [Fact]
    public void Placing_the_same_window_again_updates_its_memory_instead_of_adding_another()
    {
        var rules = new List<PlacementRule>();
        PlacementRules.Remember(rules, "notepad++", "a", ZonePlacement("right"));

        Assert.True(PlacementRules.Remember(rules, "NOTEPAD++", "A", AreaPlacement(4, 4, new CellSpan(1, 1, 2, 3), monitor: 2)));

        Assert.Equal(2, rules.Count);
        Assert.All(rules, rule =>
        {
            Assert.Equal(string.Empty, rule.ZoneId);
            Assert.True(rule.HasArea);
            Assert.Equal((4, 4), (rule.Columns, rule.Rows));
            Assert.Equal(new CellSpan(1, 1, 2, 3), rule.AreaAsZone().Span);
            Assert.Equal(2, rule.Monitor);
        });
    }

    [Fact]
    public void Placing_in_the_same_place_again_changes_nothing()
    {
        var rules = new List<PlacementRule>();
        PlacementRules.Remember(rules, "notepad++", "a", ZonePlacement("right"));

        Assert.False(PlacementRules.Remember(rules, "notepad++", "a", ZonePlacement("right")));
    }

    [Fact]
    public void Placing_switches_a_remembered_placement_back_on()
    {
        var rules = new List<PlacementRule>();
        PlacementRules.Remember(rules, "notepad++", "a", ZonePlacement("right"));
        rules.ForEach(rule => rule.Enabled = false);

        Assert.True(PlacementRules.Remember(rules, "notepad++", "a", ZonePlacement("right")));
        Assert.All(rules, rule => Assert.True(rule.Enabled));
    }

    [Fact]
    public void A_window_without_a_title_is_remembered_for_its_program_only()
    {
        var rules = new List<PlacementRule>();

        Assert.True(PlacementRules.Remember(rules, "tool", "   ", ZonePlacement("right")));

        var rule = Assert.Single(rules);
        Assert.Equal(string.Empty, rule.Title);
    }

    [Fact]
    public void Only_the_most_recently_placed_titles_of_a_program_are_kept()
    {
        var rules = new List<PlacementRule>();
        int total = PlacementRules.MaxRememberedTitles + 5;
        for (int i = 0; i < total; i++)
        {
            PlacementRules.Remember(rules, "word", $"Document {i}", ZonePlacement("right"));
        }

        // Placing an old window again makes it recent, so it is not the next to go.
        PlacementRules.Remember(rules, "word", "Document 5", ZonePlacement("left"));
        PlacementRules.Remember(rules, "word", "One more", ZonePlacement("right"));

        // Another program's titles are counted separately.
        PlacementRules.Remember(rules, "excel", "Budget", ZonePlacement("right"));

        var titles = rules.Where(rule => rule.Program == "word" && rule.Title.Length > 0).Select(rule => rule.Title).ToList();
        Assert.Equal(PlacementRules.MaxRememberedTitles, titles.Count);
        Assert.DoesNotContain("Document 0", titles);
        Assert.DoesNotContain("Document 6", titles);
        Assert.Contains("Document 5", titles);
        Assert.Contains("One more", titles);
        Assert.Single(rules, rule => rule.Program == "word" && rule.Title.Length == 0);
        Assert.Single(rules, rule => rule.Program == "excel" && rule.Title == "Budget");
    }

    [Fact]
    public void Only_a_remembered_placement_keeps_an_exact_title()
    {
        var remembered = new PlacementRule { Program = "x", Learned = true, Title = "  Exact  ", TitleContains = "ignored", ZoneId = "z" };
        var written = new PlacementRule { Program = "x", Title = "Exact", TitleContains = "part", ZoneId = "z" };

        remembered.Normalize();
        written.Normalize();

        Assert.Equal("Exact", remembered.Title);
        Assert.Equal(string.Empty, remembered.TitleContains);
        Assert.Equal(string.Empty, written.Title);
        Assert.Equal("part", written.TitleContains);
    }

    [Fact]
    public void Nothing_is_remembered_for_a_window_a_written_rule_already_covers()
    {
        var written = Rule("excel", "budget");
        var rules = new List<PlacementRule> { written };

        Assert.False(PlacementRules.Remember(rules, "excel", "Budget.xlsx", ZonePlacement("right")));
        Assert.Single(rules);
        Assert.Equal("z", written.ZoneId);

        // Another Excel window, which the written rule does not cover, is remembered.
        Assert.True(PlacementRules.Remember(rules, "excel", "Report.xlsx", ZonePlacement("right")));
        Assert.Equal(3, rules.Count);

        // The written rule still decides for the window it covers.
        Assert.Same(written, PlacementRules.Find(rules, "excel", "Budget.xlsx"));
    }

    [Fact]
    public void A_written_rule_that_is_switched_off_does_not_stop_remembering()
    {
        var rules = new List<PlacementRule> { Rule("excel", enabled: false) };

        Assert.True(PlacementRules.Remember(rules, "excel", "x", ZonePlacement("right")));
    }

    [Fact]
    public void A_placement_without_a_program_or_a_target_is_not_remembered()
    {
        var rules = new List<PlacementRule>();

        Assert.False(PlacementRules.Remember(rules, "  ", "x", ZonePlacement("right")));
        Assert.False(PlacementRules.Remember(rules, "excel", "x", new PlacementRule()));
        Assert.Empty(rules);
    }

    [Fact]
    public void A_rule_goes_to_its_zone_or_to_its_own_area()
    {
        var zone = new Zone { Id = "right", Columns = 2, Rows = 1, Column0 = 1, Column1 = 1 };
        Zone? Find(string id) => id == "right" ? zone : null;
        var area = new RectI(0, 0, 1920, 1040);

        Assert.Same(zone, PlacementRules.TargetOf(ZonePlacement("right"), Find));
        Assert.Null(PlacementRules.TargetOf(ZonePlacement("deleted"), Find));

        var own = PlacementRules.TargetOf(AreaPlacement(4, 4, new CellSpan(1, 1, 2, 3)), Find);
        Assert.NotNull(own);
        Assert.Equal(new RectI(480, 260, 1440, 1040), own!.BoundsOn(area, 0, 0));
    }

    [Fact]
    public void An_area_on_a_very_fine_grid_survives()
    {
        var placement = AreaPlacement(400, 400, new CellSpan(399, 0, 399, 399));
        placement.Normalize();

        Assert.Equal((400, 400), (placement.Columns, placement.Rows));
        Assert.Equal(new CellSpan(399, 0, 399, 399), placement.AreaAsZone().Span);
    }

    [Fact]
    public void Naming_a_zone_drops_the_area_and_the_other_way_round()
    {
        var rule = AreaPlacement(4, 4, new CellSpan(1, 1, 2, 3));
        Assert.True(rule.HasArea);

        rule.SetZone("right");
        Assert.False(rule.HasArea);
        Assert.Equal(0, rule.Columns);

        rule.SetArea(2, 2, new CellSpan(0, 0, 0, 1));
        Assert.Equal(string.Empty, rule.ZoneId);
        Assert.True(rule.HasArea);
    }

    [Fact]
    public void Remembered_placements_are_saved_and_loaded()
    {
        string path = Path.Combine(_dir, "settings.json");
        var settings = new Settings { RememberPlacements = false };
        PlacementRules.Remember(settings.Rules, "notepad++", "a", AreaPlacement(4, 4, new CellSpan(1, 1, 2, 3), monitor: 2));

        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);

        Assert.False(loaded.RememberPlacements);
        Assert.Equal(2, loaded.Rules.Count);
        Assert.Single(loaded.Rules, rule => rule.Title == "a");
        Assert.Single(loaded.Rules, rule => rule.Title.Length == 0);
        Assert.All(loaded.Rules, rule =>
        {
            Assert.True(rule.Learned);
            Assert.True(rule.HasArea);
            Assert.Equal(new CellSpan(1, 1, 2, 3), rule.AreaAsZone().Span);
            Assert.Equal(2, rule.Monitor);
        });
        Assert.DoesNotContain("\"HasArea\"", File.ReadAllText(path));
        Assert.DoesNotContain("\"Rank\"", File.ReadAllText(path));
    }

    [Theory]
    [InlineData(0, 2, 1, 1)]   // no monitor named: stay where it opened
    [InlineData(1, 2, 1, 0)]   // monitor 1
    [InlineData(2, 2, 0, 1)]   // monitor 2
    [InlineData(3, 2, 1, 1)]   // monitor 3 is not connected: stay where it opened
    [InlineData(2, 1, 0, 0)]   // only one monitor left
    [InlineData(0, 2, 7, 1)]   // an out-of-range opening monitor is pulled back
    public void Monitor_choice(int ruleMonitor, int monitorCount, int openedOn, int expected)
    {
        var rule = new PlacementRule { Program = "x", Monitor = ruleMonitor };

        Assert.Equal(expected, PlacementRules.ResolveMonitor(rule, monitorCount, openedOn));
    }

    [Fact]
    public void Zones_get_identifiers_that_stay_the_same()
    {
        string path = Path.Combine(_dir, "settings.json");
        var settings = new Settings();
        settings.Zones.Add(new Zone { Name = "A" });
        settings.Zones.Add(new Zone { Name = "B" });
        settings.Normalize();

        string idA = settings.Zones[0].Id;
        string idB = settings.Zones[1].Id;
        Assert.Equal(8, idA.Length);
        Assert.NotEqual(idA, idB);

        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);

        Assert.Equal(idA, loaded.Zones[0].Id);
        Assert.Equal(idB, loaded.Zones[1].Id);
        Assert.Same(loaded.Zones[1], loaded.FindZone(idB));
        Assert.Null(loaded.FindZone("nope"));
    }

    [Fact]
    public void A_copied_zone_gets_its_own_identifier()
    {
        var settings = new Settings();
        settings.Zones.Add(new Zone { Id = "same", Name = "A" });
        settings.Zones.Add(new Zone { Id = "same", Name = "B" });

        settings.Normalize();

        Assert.Equal("same", settings.Zones[0].Id);
        Assert.NotEqual("same", settings.Zones[1].Id);
    }

    [Fact]
    public void Rules_are_saved_and_loaded()
    {
        string path = Path.Combine(_dir, "settings.json");
        var settings = new Settings { ApplyRulesOnOpen = false };
        settings.Zones.Add(new Zone { Id = "right", Name = "Right half", Columns = 2, Column0 = 1, Column1 = 1 });
        settings.Rules.Add(new PlacementRule { Program = "notepad++.exe", TitleContains = "todo", ZoneId = "right", Monitor = 2 });
        settings.Rules.Add(new PlacementRule { Program = "   ", ZoneId = "right" });

        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);

        Assert.False(loaded.ApplyRulesOnOpen);
        var rule = Assert.Single(loaded.Rules);
        Assert.Equal("notepad++", rule.Program);
        Assert.Equal("todo", rule.TitleContains);
        Assert.Equal("right", rule.ZoneId);
        Assert.Equal(2, rule.Monitor);
        Assert.True(rule.Enabled);
    }

    [Fact]
    public void A_rule_whose_zone_was_deleted_is_kept()
    {
        var settings = new Settings();
        settings.Rules.Add(new PlacementRule { Program = "excel", ZoneId = "gone" });

        settings.Normalize();

        Assert.Single(settings.Rules);
        Assert.Null(settings.FindZone(settings.Rules[0].ZoneId));
    }

    [Fact]
    public void Copying_settings_copies_rules_rather_than_sharing_them()
    {
        var source = new Settings();
        source.Rules.Add(new PlacementRule { Program = "excel", ZoneId = "z" });
        var copy = new Settings();

        copy.CopyFrom(source);
        copy.Rules[0].Program = "changed";

        Assert.Equal("excel", source.Rules[0].Program);
    }
}
