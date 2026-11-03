namespace AutoTyper.Mac.Tests;

public class MacHotkeyTests
{
    [Fact]
    public void Default_FormatsAsControlOptionT()
    {
        Assert.Equal("Control+Option+T", MacHotkey.Default.ToString());
    }

    [Theory]
    [InlineData(MacModifiers.Control | MacModifiers.Option, MacKey.T)]
    [InlineData(MacModifiers.Command | MacModifiers.Shift, MacKey.F9)]
    [InlineData(MacModifiers.None, MacKey.F8)]
    [InlineData(MacModifiers.Control | MacModifiers.Option | MacModifiers.Shift | MacModifiers.Command, MacKey.D1)]
    public void FormatThenParse_RoundTrips(MacModifiers modifiers, MacKey key)
    {
        var hotkey = new MacHotkey(modifiers, key);

        Assert.True(MacHotkey.TryParse(hotkey.ToString(), out var parsed));
        Assert.Equal(hotkey, parsed);
    }

    [Fact]
    public void ToString_UsesFixedModifierOrder()
    {
        var hotkey = new MacHotkey(
            MacModifiers.Command | MacModifiers.Shift | MacModifiers.Option | MacModifiers.Control,
            MacKey.D1);

        Assert.Equal("Control+Option+Shift+Command+1", hotkey.ToString());
    }

    [Fact]
    public void TryParse_IsCaseInsensitive()
    {
        Assert.True(MacHotkey.TryParse("control+OPTION+t", out var hotkey));
        Assert.Equal(MacHotkey.Default, hotkey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Banana")]
    [InlineData("Control+")]
    [InlineData("Control+Option")]
    [InlineData("T")]
    [InlineData("Ctrl+Alt+T")]
    [InlineData("Control+F13")]
    public void TryParse_RejectsGarbage(string? text)
    {
        Assert.False(MacHotkey.TryParse(text, out _));
    }

    [Theory]
    [InlineData(MacModifiers.None, MacKey.T, false)]
    [InlineData(MacModifiers.None, MacKey.F8, true)]
    [InlineData(MacModifiers.None, MacKey.F12, true)]
    [InlineData(MacModifiers.Control, MacKey.T, true)]
    [InlineData(MacModifiers.None, MacKey.Space, false)]
    [InlineData(MacModifiers.Command, MacKey.Space, true)]
    public void IsValid_RequiresModifierUnlessFKey(MacModifiers modifiers, MacKey key, bool expected)
    {
        Assert.Equal(expected, new MacHotkey(modifiers, key).IsValid);
    }

    [Fact]
    public void Masks_MapEachModifier()
    {
        var controlOption = new MacHotkey(MacModifiers.Control | MacModifiers.Option, MacKey.T);
        Assert.Equal(0x1800u, controlOption.CarbonModifiers);
        Assert.Equal(0xC0000UL, controlOption.EventFlagMask);

        var commandShift = new MacHotkey(MacModifiers.Command | MacModifiers.Shift, MacKey.T);
        Assert.Equal(0x300u, commandShift.CarbonModifiers);
        Assert.Equal(0x120000UL, commandShift.EventFlagMask);
    }

    [Theory]
    [InlineData(MacKey.T, 0x11u)]
    [InlineData(MacKey.A, 0x00u)]
    [InlineData(MacKey.F1, 0x7Au)]
    [InlineData(MacKey.Space, 0x31u)]
    [InlineData(MacKey.D0, 0x1Du)]
    public void KeyCode_IsTheVirtualKeyCode(MacKey key, uint expected)
    {
        Assert.Equal(expected, new MacHotkey(MacModifiers.Control, key).KeyCode);
    }
}
