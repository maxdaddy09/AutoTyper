namespace AutoTyper.Tests;

public class HotkeyBindingTests
{
    [Fact]
    public void Default_FormatsAsCtrlAltT()
    {
        Assert.Equal("Ctrl+Alt+T", HotkeyBinding.Default.ToString());
    }

    [Fact]
    public void FormatThenParse_RoundTrips()
    {
        var bindings = new[]
        {
            new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.T),
            new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.F9),
            new HotkeyBinding(HotkeyModifiers.None, Keys.F8),
            new HotkeyBinding(HotkeyModifiers.Win | HotkeyModifiers.Shift, Keys.D1),
        };

        foreach (var binding in bindings)
        {
            Assert.True(HotkeyBinding.TryParse(binding.ToString(), out var parsed), binding.ToString());
            Assert.Equal(binding, parsed);
        }
    }

    [Fact]
    public void ToString_ListsModifiersInFixedOrder_AndDigitsWithoutD()
    {
        var binding = new HotkeyBinding(
            HotkeyModifiers.Win | HotkeyModifiers.Shift | HotkeyModifiers.Alt | HotkeyModifiers.Control,
            Keys.D1);

        Assert.Equal("Ctrl+Alt+Shift+Win+1", binding.ToString());
    }

    [Theory]
    [InlineData("ctrl+alt+t")]
    [InlineData("CTRL+ALT+T")]
    public void TryParse_IsCaseInsensitive(string text)
    {
        Assert.True(HotkeyBinding.TryParse(text, out var parsed));
        Assert.Equal(HotkeyBinding.Default, parsed);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Banana")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Alt")]
    [InlineData("Ctrl+Banana+T")]
    [InlineData("T")]
    public void TryParse_RejectsGarbage(string? text)
    {
        Assert.False(HotkeyBinding.TryParse(text, out _));
    }

    [Theory]
    [InlineData(HotkeyModifiers.None, Keys.T, false)]
    [InlineData(HotkeyModifiers.None, Keys.F8, true)]
    [InlineData(HotkeyModifiers.None, Keys.F24, true)]
    [InlineData(HotkeyModifiers.Control, Keys.T, true)]
    [InlineData(HotkeyModifiers.Control, Keys.ControlKey, false)]
    [InlineData(HotkeyModifiers.None, Keys.None, false)]
    public void IsValid_RequiresModifierUnlessFKey(HotkeyModifiers modifiers, Keys key, bool expected)
    {
        Assert.Equal(expected, new HotkeyBinding(modifiers, key).IsValid);
    }

    [Fact]
    public void NativeModifiers_AddsNoRepeat()
    {
        var binding = new HotkeyBinding(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.T);

        Assert.Equal(0x0002u | 0x0001u | 0x4000u, binding.NativeModifiers);
    }

    [Fact]
    public void NativeKey_IsTheVirtualKeyCode()
    {
        Assert.Equal((uint)Keys.T, HotkeyBinding.Default.NativeKey);
    }
}
