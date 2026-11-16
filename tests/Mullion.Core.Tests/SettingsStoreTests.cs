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

        Assert.Equal(4, settings.Columns);
        Assert.Equal(4, settings.Rows);
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

        Assert.Equal(4, settings.Columns);
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
