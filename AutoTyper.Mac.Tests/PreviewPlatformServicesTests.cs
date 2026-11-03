namespace AutoTyper.Mac.Tests;

public class PreviewPlatformServicesTests
{
    [Fact]
    public void IsPreview_IsTrue()
    {
        Assert.True(new PreviewPlatformServices().IsPreview);
    }

    [Fact]
    public void Permission_IsGranted_AndOpeningTheSettingsPageDoesNothing()
    {
        var platform = new PreviewPlatformServices();

        Assert.True(platform.HasInputPermission());
        platform.OpenPermissionSettings();
    }

    [Theory]
    [InlineData(MacModifiers.Control)]
    [InlineData(MacModifiers.Option | MacModifiers.Shift)]
    [InlineData(MacModifiers.Command)]
    [InlineData(MacModifiers.None)]
    public void Modifiers_AreNeverDown(MacModifiers modifiers)
    {
        Assert.False(new PreviewPlatformServices().AreModifiersDown(modifiers));
    }

    [Fact]
    public void KeySender_RecordsWhatItIsAskedToType_InsteadOfTypingIt()
    {
        var sender = new PreviewPlatformServices().CreateKeySender();

        Assert.True(sender.SendText("héllo"));
        Assert.True(sender.SendEnter());
        Assert.True(sender.SendTab());

        var recorder = Assert.IsType<PreviewPlatformServices.RecordingKeySender>(sender);
        Assert.Equal(new[] { "T:héllo", "Enter", "Tab" }, recorder.Sent);
    }

    [Fact]
    public void Hotkey_AcceptsAnyValidCombination_AndKeepsTheLastOne()
    {
        using var hotkey = new PreviewPlatformServices().CreateHotkey();
        Assert.Null(hotkey.Current);

        var first = new MacHotkey(MacModifiers.Command | MacModifiers.Shift, MacKey.K);
        var second = new MacHotkey(MacModifiers.None, MacKey.F5);

        Assert.True(hotkey.TryRegister(first));
        Assert.Equal(first, hotkey.Current);
        Assert.True(hotkey.TryRegister(second));
        Assert.Equal(second, hotkey.Current);
    }

    [Fact]
    public void Hotkey_RefusesAnInvalidCombination_AndKeepsThePreviousOne()
    {
        using var hotkey = new PreviewPlatformServices().CreateHotkey();
        var valid = MacHotkey.Default;
        Assert.True(hotkey.TryRegister(valid));

        Assert.False(hotkey.TryRegister(new MacHotkey(MacModifiers.None, MacKey.A)));

        Assert.Equal(valid, hotkey.Current);
    }

    [Fact]
    public void Hotkey_NeverFires()
    {
        using var hotkey = new PreviewPlatformServices().CreateHotkey();
        var fired = 0;
        hotkey.Pressed += () => fired++;

        hotkey.TryRegister(MacHotkey.Default);
        hotkey.TryRegister(new MacHotkey(MacModifiers.Command, MacKey.Space));

        Assert.Equal(0, fired);
    }

    [Fact]
    public void Dispose_ReleasesTheHotkey()
    {
        var hotkey = new PreviewPlatformServices().CreateHotkey();
        hotkey.TryRegister(MacHotkey.Default);

        hotkey.Dispose();

        Assert.Null(hotkey.Current);
    }

    [Fact]
    public void EachCreateHotkeyCall_GivesAnIndependentHotkey()
    {
        var platform = new PreviewPlatformServices();
        using var one = platform.CreateHotkey();
        using var two = platform.CreateHotkey();

        one.TryRegister(MacHotkey.Default);

        Assert.Null(two.Current);
    }
}
