using Mullion.Core;

namespace Mullion.Core.Tests;

public class HotkeyMatcherTests
{
    private const int C = 0x43;
    private const int V = 0x56;
    private const int LeftCtrl = 0xA2;
    private const HotkeyModifiers CtrlShift = HotkeyModifiers.Ctrl | HotkeyModifiers.Shift;

    private static HotkeyMatcher WithZone(Hotkey hotkey, int zone)
    {
        var matcher = new HotkeyMatcher();
        matcher.SetZones(new Dictionary<Hotkey, int> { [hotkey] = zone });
        return matcher;
    }

    [Fact]
    public void Nothing_to_watch_means_idle()
    {
        var matcher = new HotkeyMatcher();
        Assert.True(matcher.IsIdle);

        matcher.Recording = true;
        Assert.False(matcher.IsIdle);

        matcher.Recording = false;
        matcher.SetZones(new Dictionary<Hotkey, int> { [new Hotkey(CtrlShift, C)] = 0 });
        Assert.False(matcher.IsIdle);
    }

    [Fact]
    public void A_zone_shortcut_is_recognised_and_hidden()
    {
        var matcher = WithZone(new Hotkey(CtrlShift, C), 3);

        Assert.Equal(new KeyDecision(true, Zone: 3), matcher.KeyDown(C, CtrlShift));
        Assert.True(matcher.KeyUp(C));
        Assert.False(matcher.KeyUp(C));
    }

    [Fact]
    public void Holding_the_key_fires_once()
    {
        var matcher = WithZone(new Hotkey(CtrlShift, C), 3);

        Assert.Equal(3, matcher.KeyDown(C, CtrlShift).Zone);

        var repeat = matcher.KeyDown(C, CtrlShift);
        Assert.True(repeat.Swallow);
        Assert.Null(repeat.Zone);

        matcher.KeyUp(C);
        Assert.Equal(3, matcher.KeyDown(C, CtrlShift).Zone);
    }

    [Theory]
    [InlineData(HotkeyModifiers.Ctrl)]
    [InlineData(HotkeyModifiers.Ctrl | HotkeyModifiers.Shift | HotkeyModifiers.Alt)]
    [InlineData(HotkeyModifiers.None)]
    public void Modifiers_must_match_exactly(HotkeyModifiers held)
    {
        var matcher = WithZone(new Hotkey(CtrlShift, C), 3);

        Assert.Equal(default, matcher.KeyDown(C, held));
        Assert.False(matcher.KeyUp(C));
    }

    [Fact]
    public void Other_keys_and_modifier_keys_pass_through()
    {
        var matcher = WithZone(new Hotkey(CtrlShift, C), 3);

        Assert.Equal(default, matcher.KeyDown(V, CtrlShift));
        Assert.Equal(default, matcher.KeyDown(LeftCtrl, HotkeyModifiers.None));
    }

    [Fact]
    public void Recording_reports_the_combination_instead_of_running_the_zone()
    {
        var matcher = WithZone(new Hotkey(CtrlShift, C), 3);
        matcher.Recording = true;

        var decision = matcher.KeyDown(C, CtrlShift);

        Assert.True(decision.Swallow);
        Assert.Null(decision.Zone);
        Assert.Equal(new Hotkey(CtrlShift, C), decision.Captured);
        Assert.True(matcher.KeyUp(C));
    }

    [Fact]
    public void Recording_leaves_keys_without_ctrl_alt_or_win_to_the_shortcut_box()
    {
        var matcher = new HotkeyMatcher { Recording = true };

        Assert.Equal(default, matcher.KeyDown(C, HotkeyModifiers.Shift));
        Assert.Equal(default, matcher.KeyDown(0x1B, HotkeyModifiers.None));
        Assert.Equal(default, matcher.KeyDown(0x09, HotkeyModifiers.None));
        Assert.Equal(default, matcher.KeyDown(0x08, HotkeyModifiers.None));
    }

    [Fact]
    public void Recording_ignores_keys_that_cannot_be_in_a_shortcut()
    {
        var matcher = new HotkeyMatcher { Recording = true };

        // Escape has no name in a shortcut, even with Ctrl held.
        Assert.Equal(default, matcher.KeyDown(0x1B, HotkeyModifiers.Ctrl));
    }

    [Theory]
    [InlineData(0x10, true)]
    [InlineData(0x11, true)]
    [InlineData(0x12, true)]
    [InlineData(0x5B, true)]
    [InlineData(0x5C, true)]
    [InlineData(0xA0, true)]
    [InlineData(0xA5, true)]
    [InlineData(0x43, false)]
    [InlineData(0x20, false)]
    public void Modifier_keys_are_recognised(int key, bool expected)
    {
        Assert.Equal(expected, Hotkey.IsModifierKey(key));
    }
}
