using Mullion.Core;

namespace Mullion.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mullion-tests-" + Guid.NewGuid().ToString("N"));

    public SettingsStoreTests()
    {
        Directory.CreateDirectory(_dir);
    }

    private string FilePath => Path.Combine(_dir, "settings.json");

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var settings = SettingsStore.Load(FilePath);

        Assert.Equal(60, settings.Columns);
        Assert.Equal(60, settings.Rows);
        Assert.Equal(0, settings.WindowGap);
        Assert.Equal(SnapModifier.Ctrl, settings.SnapMoveModifier);
    }

    [Fact]
    public void Saved_settings_load_back_unchanged()
    {
        var settings = new Settings
        {
            Columns = 6,
            Rows = 3,
            WindowGap = 8,
            EdgeGap = 12,
            SnapMoveModifier = SnapModifier.Alt,
        };
        settings.Appearance.SelectionColor = "#FF8800";
        settings.Appearance.TintOpacity = 0.1;
        settings.Appearance.ShowSizeLabel = false;

        SettingsStore.Save(FilePath, settings);
        var loaded = SettingsStore.Load(FilePath);

        Assert.Equal(6, loaded.Columns);
        Assert.Equal(3, loaded.Rows);
        Assert.Equal(8, loaded.WindowGap);
        Assert.Equal(12, loaded.EdgeGap);
        Assert.Equal(SnapModifier.Alt, loaded.SnapMoveModifier);
        Assert.Equal("#FF8800", loaded.Appearance.SelectionColor);
        Assert.Equal(0.1, loaded.Appearance.TintOpacity);
        Assert.False(loaded.Appearance.ShowSizeLabel);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void An_exported_file_imports_back_whole()
    {
        var settings = new Settings { Columns = 6, Rows = 3, GridButton = TriggerButton.Middle, CheckForUpdates = false };
        settings.Zones.Add(new Zone { Name = "Left", Columns = 2, Rows = 1, Shortcut = "Ctrl+Alt+1" });
        settings.Rules.Add(new PlacementRule { Program = "excel", ZoneId = settings.Zones[0].Id });
        settings.ExcludedPrograms.Add("game");
        SettingsStore.Save(FilePath, settings);

        var imported = SettingsStore.TryReadForImport(FilePath);

        Assert.NotNull(imported);
        Assert.Equal((6, 3), (imported.Columns, imported.Rows));
        Assert.Equal(TriggerButton.Middle, imported.GridButton);
        Assert.False(imported.CheckForUpdates);
        Assert.Equal("Left", Assert.Single(imported.Zones).Name);
        Assert.Equal("excel", Assert.Single(imported.Rules).Program);
        Assert.Equal(["game"], imported.ExcludedPrograms);
    }

    [Fact]
    public void A_hand_written_file_with_only_some_settings_can_be_imported()
    {
        File.WriteAllText(FilePath, "{ \"columns\": 8 } // the rest stays as it comes");

        var imported = SettingsStore.TryReadForImport(FilePath);

        Assert.NotNull(imported);
        Assert.Equal(8, imported.Columns);
        Assert.Equal(Settings.DefaultGridSize, imported.Rows);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1, 2, 3]")]
    [InlineData("{}")]
    [InlineData("{ \"name\": \"some other program's file\", \"version\": 3 }")]
    [InlineData("{ \"columns\": \"many\" }")]
    public void A_file_that_is_not_mullions_is_refused_and_left_alone(string content)
    {
        File.WriteAllText(FilePath, content);

        Assert.Null(SettingsStore.TryReadForImport(FilePath));

        // Unlike loading Mullion's own file, importing never moves the file aside.
        Assert.Equal(content, File.ReadAllText(FilePath));
        Assert.False(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void A_missing_file_cannot_be_imported()
    {
        Assert.Null(SettingsStore.TryReadForImport(Path.Combine(_dir, "nothing.json")));
    }

    [Fact]
    public void How_mullion_runs_and_updates_is_copied_with_the_rest()
    {
        var source = new Settings { RunAsAdministrator = true, CheckForUpdates = false };
        var copy = new Settings();

        copy.CopyFrom(source);

        Assert.True(copy.RunAsAdministrator);
        Assert.False(copy.CheckForUpdates);
    }

    [Fact]
    public void Enum_values_are_stored_as_readable_text()
    {
        SettingsStore.Save(FilePath, new Settings { SnapMoveModifier = SnapModifier.Shift });

        Assert.Contains("\"Shift\"", File.ReadAllText(FilePath));
    }

    [Fact]
    public void Corrupt_file_gives_defaults_and_is_kept_aside()
    {
        File.WriteAllText(FilePath, "{ this is not json");

        var settings = SettingsStore.Load(FilePath);

        Assert.Equal(Settings.DefaultGridSize, settings.Columns);
        Assert.True(File.Exists(FilePath + ".bad"));
        Assert.False(File.Exists(FilePath));
    }

    [Fact]
    public void Out_of_range_values_are_pulled_back_into_range()
    {
        File.WriteAllText(FilePath, """
            {
              // hand-edited
              "columns": 500,
              "rows": 0,
              "windowGap": -5,
              "edgeGap": 9999,
              "appearance": { "tintColor": "red", "tintOpacity": 7, "lineColor": "#abcdef" },
            }
            """);

        var settings = SettingsStore.Load(FilePath);

        Assert.Equal(Settings.MaxGridSize, settings.Columns);
        Assert.Equal(1, settings.Rows);
        Assert.Equal(0, settings.WindowGap);
        Assert.Equal(Settings.MaxGap, settings.EdgeGap);
        Assert.Equal("#000000", settings.Appearance.TintColor);
        Assert.Equal(1, settings.Appearance.TintOpacity);
        Assert.Equal("#ABCDEF", settings.Appearance.LineColor);
    }

    [Fact]
    public void Grid_button_and_cancel_key_default_to_right_and_Esc()
    {
        var settings = SettingsStore.Load(FilePath);

        Assert.Equal(TriggerButton.Right, settings.GridButton);
        Assert.Equal("Esc", settings.CancelKey);
        Assert.Equal(0x1B, settings.CancelKeyCode);
    }

    [Fact]
    public void Grid_button_and_cancel_key_are_saved_and_loaded()
    {
        SettingsStore.Save(FilePath, new Settings { GridButton = TriggerButton.Middle, CancelKey = "q" });

        var loaded = SettingsStore.Load(FilePath);

        Assert.Equal(TriggerButton.Middle, loaded.GridButton);
        Assert.Equal("Q", loaded.CancelKey);
        Assert.Equal(0x51, loaded.CancelKeyCode);
        Assert.Contains("\"Middle\"", File.ReadAllText(FilePath));
    }

    [Theory]
    [InlineData("escape", "Esc", 0x1B)]
    [InlineData(" backspace ", "Backspace", 0x08)]
    [InlineData("f2", "F2", 0x71)]
    [InlineData("space", "Space", 0x20)]
    [InlineData("Ctrl+Q", "Esc", 0x1B)]
    [InlineData("nonsense", "Esc", 0x1B)]
    [InlineData("", "Esc", 0x1B)]
    [InlineData(null, "Esc", 0x1B)]
    public void Cancel_key_is_one_known_key_or_falls_back_to_Esc(string? written, string expected, int code)
    {
        var settings = new Settings { CancelKey = written! };

        settings.Normalize();

        Assert.Equal(expected, settings.CancelKey);
        Assert.Equal(code, settings.CancelKeyCode);
    }

    [Fact]
    public void Every_single_key_name_reads_back_as_the_same_key()
    {
        for (int key = 0; key < 256; key++)
        {
            if (Hotkey.SingleKeyName(key) is { } name)
            {
                Assert.True(Hotkey.TryParseSingleKey(name, out int parsed), name);
                Assert.Equal(key, parsed);
            }
        }
    }

    [Fact]
    public void Grid_can_have_up_to_one_hundred_columns_and_rows()
    {
        SettingsStore.Save(FilePath, new Settings { Columns = 100, Rows = 100 });

        var settings = SettingsStore.Load(FilePath);

        Assert.Equal(100, settings.Columns);
        Assert.Equal(100, settings.Rows);
    }

    [Fact]
    public void Excluded_programs_are_tidied_saved_and_loaded()
    {
        var settings = new Settings
        {
            ExcludedPrograms = ["  Photoshop.exe ", "chrome", "CHROME.EXE", "", "anydesk"],
        };

        SettingsStore.Save(FilePath, settings);
        var loaded = SettingsStore.Load(FilePath);

        Assert.Equal(["anydesk", "chrome", "Photoshop"], loaded.ExcludedPrograms);
        Assert.True(loaded.IsExcluded("photoshop"));
        Assert.True(loaded.IsExcluded("Chrome.exe"));
        Assert.False(loaded.IsExcluded("notepad"));
        Assert.False(loaded.IsExcluded(""));
    }

    [Fact]
    public void A_missing_exclusion_list_is_treated_as_empty()
    {
        File.WriteAllText(FilePath, """{ "excludedPrograms": null }""");

        var settings = SettingsStore.Load(FilePath);

        Assert.Empty(settings.ExcludedPrograms);
        Assert.False(settings.IsExcluded("chrome"));
    }

    [Fact]
    public void Null_appearance_is_replaced_with_defaults()
    {
        File.WriteAllText(FilePath, """{ "appearance": null }""");

        var settings = SettingsStore.Load(FilePath);

        Assert.NotNull(settings.Appearance);
        Assert.Equal("#3B82F6", settings.Appearance.SelectionColor);
    }

    [Theory]
    [InlineData("#3B82F6", true)]
    [InlineData("#abcdef", true)]
    [InlineData("3B82F6", false)]
    [InlineData("#3B82F", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Colour_validation(string? value, bool expected)
    {
        Assert.Equal(expected, AppearanceSettings.IsValidColor(value));
    }
}
