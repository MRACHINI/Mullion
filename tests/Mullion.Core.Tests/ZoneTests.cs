using Mullion.Core;

namespace Mullion.Core.Tests;

public sealed class ZoneTests : IDisposable
{
    private static readonly RectI Left = new(0, 0, 1920, 1040);
    private static readonly RectI Right = new(1920, 0, 4480, 1400);
    private const nint Window = 0x42;

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "mullion-zones-" + Guid.NewGuid().ToString("N"));

    public ZoneTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    private static Zone RightHalf() => new() { Name = "Right half", Columns = 2, Rows = 1, Column0 = 1, Column1 = 1 };

    [Theory]
    [InlineData("Ctrl+Alt+1", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x31)]
    [InlineData("ctrl+alt+a", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x41)]
    [InlineData("Alt+Ctrl+Shift+F12", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Shift, 0x7B)]
    [InlineData("Win+Left", HotkeyModifiers.Win, 0x25)]
    [InlineData("Ctrl+Num+", HotkeyModifiers.Ctrl, 0x6B)]
    [InlineData("Ctrl+=", HotkeyModifiers.Ctrl, 0xBB)]
    [InlineData(" Ctrl + Alt + Space ", HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x20)]
    public void Shortcut_text_is_read(string text, HotkeyModifiers modifiers, int key)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(new Hotkey(modifiers, key), hotkey);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+NoSuchKey")]
    [InlineData("Hyper+A")]
    public void Unreadable_shortcut_text_is_rejected(string? text)
    {
        Assert.False(Hotkey.TryParse(text, out _));
    }

    [Fact]
    public void Shortcut_is_written_in_a_fixed_order()
    {
        var hotkey = new Hotkey(HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Ctrl, 0x31);

        Assert.Equal("Ctrl+Alt+Shift+Win+1", hotkey.ToString());
    }

    [Fact]
    public void Every_known_key_survives_writing_and_reading()
    {
        for (int key = 0; key < 256; key++)
        {
            if (Hotkey.KeyName(key) is null)
            {
                continue;
            }

            var hotkey = new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift, key);
            Assert.True(Hotkey.TryParse(hotkey.ToString(), out var parsed), hotkey.ToString());
            Assert.Equal(hotkey, parsed);
        }
    }

    [Theory]
    [InlineData("Ctrl+1", true)]
    [InlineData("Alt+F4", true)]
    [InlineData("Win+Z", true)]
    [InlineData("Shift+A", false)]
    [InlineData("A", false)]
    [InlineData("F5", false)]
    public void Shortcut_needs_ctrl_alt_or_win(string text, bool expected)
    {
        Assert.True(Hotkey.TryParse(text, out var hotkey));
        Assert.Equal(expected, hotkey.IsValid);
    }

    [Fact]
    public void Zone_bounds_use_the_zones_own_grid()
    {
        Assert.Equal(new RectI(960, 0, 1920, 1040), RightHalf().BoundsOn(Left, 0, 0));
        Assert.Equal(new RectI(3200, 0, 4480, 1400), RightHalf().BoundsOn(Right, 0, 0));
    }

    [Fact]
    public void Zone_bounds_respect_gaps()
    {
        Assert.Equal(new RectI(965, 20, 1900, 1020), RightHalf().BoundsOn(Left, windowGap: 10, edgeGap: 20));
    }

    [Fact]
    public void Zone_values_are_pulled_into_range()
    {
        var zone = new Zone
        {
            Name = "  " + new string('x', 60) + "  ",
            Columns = 500,
            Rows = 0,
            Column0 = 300,
            Column1 = -4,
            Row0 = 9,
            Row1 = 9,
            Shortcut = "shift+a",
        };

        zone.Normalize();

        Assert.Equal(Zone.MaxNameLength, zone.Name.Length);
        Assert.Equal(Settings.MaxGridSize, zone.Columns);
        Assert.Equal(1, zone.Rows);
        Assert.Equal(new CellSpan(0, 0, 99, 0), zone.Span);
        Assert.Equal(string.Empty, zone.Shortcut);
        Assert.Null(zone.Hotkey);
    }

    [Fact]
    public void Zone_shortcut_text_is_tidied()
    {
        var zone = new Zone { Shortcut = "alt+ctrl+num5" };

        zone.Normalize();

        Assert.Equal("Ctrl+Alt+Num5", zone.Shortcut);
    }

    [Fact]
    public void Zones_are_saved_and_loaded()
    {
        string path = Path.Combine(_dir, "settings.json");
        var settings = new Settings { CycleMonitors = false };
        settings.Zones.Add(new Zone { Name = "Left third", Columns = 3, Rows = 1, Shortcut = "Ctrl+Alt+1" });
        settings.Zones.Add(new Zone { Name = "Middle", Columns = 3, Rows = 2, Column0 = 1, Column1 = 1, Row1 = 1 });

        SettingsStore.Save(path, settings);
        var loaded = SettingsStore.Load(path);

        Assert.False(loaded.CycleMonitors);
        Assert.Equal(2, loaded.Zones.Count);
        Assert.Equal("Left third", loaded.Zones[0].Name);
        Assert.Equal(new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x31), loaded.Zones[0].Hotkey);
        Assert.Equal(new CellSpan(1, 0, 1, 1), loaded.Zones[1].Span);
        Assert.Null(loaded.Zones[1].Hotkey);
        Assert.DoesNotContain("\"Span\"", File.ReadAllText(path));
        Assert.DoesNotContain("\"Hotkey\"", File.ReadAllText(path));
    }

    [Fact]
    public void Only_the_first_zone_keeps_a_shared_shortcut()
    {
        var settings = new Settings();
        settings.Zones.Add(new Zone { Name = "A", Shortcut = "Ctrl+Alt+1" });
        settings.Zones.Add(new Zone { Name = "B", Shortcut = "alt+ctrl+1" });

        settings.Normalize();

        Assert.Equal("Ctrl+Alt+1", settings.Zones[0].Shortcut);
        Assert.Equal(string.Empty, settings.Zones[1].Shortcut);
    }

    [Fact]
    public void Copying_settings_copies_zones_rather_than_sharing_them()
    {
        var source = new Settings();
        source.Zones.Add(RightHalf());
        var copy = new Settings();

        copy.CopyFrom(source);
        copy.Zones[0].Name = "Changed";

        Assert.Equal("Right half", source.Zones[0].Name);
    }

    [Fact]
    public void Arrange_shortcut_is_saved_tidied_and_loaded()
    {
        string path = Path.Combine(_dir, "settings.json");

        SettingsStore.Save(path, new Settings { ArrangeShortcut = "alt+ctrl+w" });
        var loaded = SettingsStore.Load(path);

        Assert.Equal("Ctrl+Alt+W", loaded.ArrangeShortcut);
        Assert.Equal(new Hotkey(HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, 0x57), loaded.ArrangeHotkey);
        Assert.DoesNotContain("\"ArrangeHotkey\"", File.ReadAllText(path));
    }

    [Fact]
    public void Arrange_shortcut_gives_way_to_a_zone_with_the_same_shortcut()
    {
        var settings = new Settings { ArrangeShortcut = "Ctrl+Alt+1" };
        settings.Zones.Add(new Zone { Name = "A", Shortcut = "Ctrl+Alt+1" });

        settings.Normalize();

        Assert.Equal("Ctrl+Alt+1", settings.Zones[0].Shortcut);
        Assert.Equal(string.Empty, settings.ArrangeShortcut);
    }

    [Fact]
    public void Unusable_arrange_shortcut_is_dropped()
    {
        var settings = new Settings { ArrangeShortcut = "Shift+A" };

        settings.Normalize();

        Assert.Equal(string.Empty, settings.ArrangeShortcut);
        Assert.Null(settings.ArrangeHotkey);
    }

    [Fact]
    public void Among_overlapping_zones_the_one_whose_centre_is_nearest_is_picked()
    {
        RectI[] zones =
        [
            new(0, 0, 960, 1040),      // left half, centre 480,520
            new(0, 0, 640, 1040),      // left third, centre 320,520
            new(960, 0, 1920, 1040),   // right half, centre 1440,520
            new(0, 0, 640, 520),       // top of the left third, centre 320,260
        ];

        Assert.Equal(3, ZoneHitTest.Pick(zones, new PointI(100, 100)));
        Assert.Equal(1, ZoneHitTest.Pick(zones, new PointI(100, 800)));
        Assert.Equal(0, ZoneHitTest.Pick(zones, new PointI(800, 800)));
        Assert.Equal(2, ZoneHitTest.Pick(zones, new PointI(1500, 100)));

        // Inside both the half and the third, but next to the half's centre.
        Assert.Equal(0, ZoneHitTest.Pick(zones, new PointI(470, 510)));

        // Inside the half, the third and the top of the third; each wins near its own centre.
        Assert.Equal(1, ZoneHitTest.Pick(zones, new PointI(320, 500)));
        Assert.Equal(3, ZoneHitTest.Pick(zones, new PointI(320, 300)));
    }

    [Fact]
    public void Every_zone_is_picked_at_its_own_centre()
    {
        RectI[] zones =
        [
            new(0, 0, 1920, 1040),
            new(0, 0, 960, 1040),
            new(0, 0, 640, 1040),
            new(0, 0, 640, 520),
            new(480, 0, 1440, 520),
            new(1280, 520, 1920, 1040),
        ];

        for (int i = 0; i < zones.Length; i++)
        {
            var centre = new PointI((zones[i].Left + zones[i].Right) / 2, (zones[i].Top + zones[i].Bottom) / 2);
            Assert.Equal(i, ZoneHitTest.Pick(zones, centre));
        }
    }

    [Fact]
    public void A_zone_centred_inside_another_wins_where_they_overlap()
    {
        RectI[] zones =
        [
            new(0, 0, 1920, 1040),     // whole screen
            new(480, 260, 1440, 780),  // centred in it
        ];

        Assert.Equal(1, ZoneHitTest.Pick(zones, new PointI(960, 520)));
        Assert.Equal(1, ZoneHitTest.Pick(zones, new PointI(500, 300)));
        Assert.Equal(0, ZoneHitTest.Pick(zones, new PointI(100, 100)));
    }

    [Fact]
    public void A_point_outside_every_zone_picks_nothing()
    {
        RectI[] zones = [new(0, 0, 960, 1040)];

        Assert.Equal(-1, ZoneHitTest.Pick(zones, new PointI(1000, 100)));
        Assert.Equal(-1, ZoneHitTest.Pick(zones, new PointI(960, 100)));
        Assert.Equal(-1, ZoneHitTest.Pick([], new PointI(0, 0)));
    }

    [Fact]
    public void Monitors_are_ordered_left_to_right()
    {
        var sorted = MonitorList.Sort([Right, default, Left]);

        Assert.Equal([Left, Right], sorted);
    }

    [Fact]
    public void A_window_belongs_to_the_monitor_it_overlaps_most()
    {
        RectI[] monitors = [Left, Right];

        Assert.Equal(0, MonitorList.IndexOf(monitors, new RectI(100, 100, 900, 700)));
        Assert.Equal(1, MonitorList.IndexOf(monitors, new RectI(1800, 100, 2600, 700)));
        Assert.Equal(1, MonitorList.IndexOf(monitors, new RectI(9000, 100, 9800, 700)));
    }

    [Fact]
    public void First_press_puts_the_window_in_the_zone_on_its_own_monitor()
    {
        var cycler = new ZoneCycler();

        var plan = cycler.Plan(Window, 0, RightHalf(), new RectI(100, 100, 900, 700), [Left, Right], 0, 0, 0, cycle: true);

        Assert.Equal((0, new RectI(960, 0, 1920, 1040)), plan);
    }

    [Fact]
    public void Pressing_again_moves_to_the_next_monitor_and_wraps_around()
    {
        var cycler = new ZoneCycler();
        RectI[] monitors = [Left, Right];
        var zone = RightHalf();

        var first = cycler.Plan(Window, 0, zone, new RectI(100, 100, 900, 700), monitors, 0, 0, 0, cycle: true);
        var second = cycler.Plan(Window, 0, zone, first.Bounds, monitors, first.Monitor, 0, 0, cycle: true);
        var third = cycler.Plan(Window, 0, zone, second.Bounds, monitors, second.Monitor, 0, 0, cycle: true);

        Assert.Equal((1, new RectI(3200, 0, 4480, 1400)), second);
        Assert.Equal(first, third);
    }

    [Fact]
    public void Pressing_again_does_nothing_new_when_cycling_is_off_or_there_is_one_monitor()
    {
        var zone = RightHalf();
        var inZone = zone.BoundsOn(Left, 0, 0);

        var off = new ZoneCycler().Plan(Window, 0, zone, inZone, [Left, Right], 0, 0, 0, cycle: false);
        var single = new ZoneCycler().Plan(Window, 0, zone, inZone, [Left], 0, 0, 0, cycle: true);

        Assert.Equal((0, inZone), off);
        Assert.Equal((0, inZone), single);
    }

    [Fact]
    public void A_window_that_refused_the_size_still_cycles_on_the_next_press()
    {
        var cycler = new ZoneCycler();
        RectI[] monitors = [Left, Right];
        var zone = RightHalf();

        var first = cycler.Plan(Window, 0, zone, new RectI(100, 100, 900, 700), monitors, 0, 0, 0, cycle: true);

        // The window took the position but stayed wider than the zone.
        var stubborn = RectI.FromSize(first.Bounds.Left, first.Bounds.Top, 1200, 800);
        var second = cycler.Plan(Window, 0, zone, stubborn, monitors, 0, 0, 0, cycle: true);

        Assert.Equal(1, second.Monitor);
    }

    [Fact]
    public void A_window_that_merely_shares_the_corner_is_not_treated_as_in_the_zone()
    {
        var cycler = new ZoneCycler();
        var zone = RightHalf();
        var corner = zone.BoundsOn(Left, 0, 0);

        var plan = cycler.Plan(Window, 0, zone, RectI.FromSize(corner.Left, corner.Top, 500, 400), [Left, Right], 0, 0, 0, cycle: true);

        Assert.Equal(0, plan.Monitor);
    }

    [Fact]
    public void A_different_zone_does_not_inherit_the_previous_press()
    {
        var cycler = new ZoneCycler();
        RectI[] monitors = [Left, Right];
        var zone = RightHalf();
        var other = new Zone { Columns = 2, Rows = 2, Column0 = 1, Column1 = 1 };

        var first = cycler.Plan(Window, 0, zone, new RectI(100, 100, 900, 700), monitors, 0, 0, 0, cycle: true);
        var second = cycler.Plan(Window, 1, other, first.Bounds, monitors, 0, 0, 0, cycle: true);

        Assert.Equal((0, new RectI(960, 0, 1920, 520)), second);
    }
}
