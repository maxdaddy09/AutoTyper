namespace AutoTyper.Mac.Tests;

// The window turns a key that MacKeyMap does not know into a MacHotkey whose key is no MacKey at all, and
// lets MainViewModel.TryChangeHotkey refuse it, so the "can't be used" text lives in one place. These
// tests pin that contract on the real preview platform.
public sealed class UnsupportedKeyTests : IDisposable
{
    const string InvalidHotkeyMessage =
        "That hotkey can't be used — hold Control, Option, Shift or Command, or use an F-key";

    // What MainWindow passes for a key MacKeyMap returned null for.
    const MacKey UnsupportedKey = (MacKey)ushort.MaxValue;

    readonly string folder = Path.Combine(Path.GetTempPath(), "AutoTyperMacTests-" + Guid.NewGuid().ToString("N"));

    MainViewModel CreateViewModel() =>
        new(new PreviewPlatformServices(), new Settings(), Path.Combine(folder, "settings.json"), () => false);

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    [Theory]
    [InlineData(MacModifiers.Control | MacModifiers.Option)]
    [InlineData(MacModifiers.Command)]
    [InlineData(MacModifiers.None)]
    public void AKeyWithNoMacKey_IsRefusedWithTheInvalidHotkeyMessage(MacModifiers modifiers)
    {
        using var viewModel = CreateViewModel();
        viewModel.Start();
        Assert.Equal("Control+Option+T", viewModel.HotkeyText);

        var accepted = viewModel.TryChangeHotkey(new MacHotkey(modifiers, UnsupportedKey));

        Assert.False(accepted);
        Assert.Equal(InvalidHotkeyMessage, viewModel.Status);
        Assert.Equal("Control+Option+T", viewModel.HotkeyText);
    }

    [Fact]
    public void AfterARefusedKey_AValidCombinationIsStillAccepted()
    {
        using var viewModel = CreateViewModel();
        viewModel.Start();
        viewModel.TryChangeHotkey(new MacHotkey(MacModifiers.Command, UnsupportedKey));

        var accepted = viewModel.TryChangeHotkey(new MacHotkey(MacModifiers.Control | MacModifiers.Option, MacKey.Y));

        Assert.True(accepted);
        Assert.Equal("Control+Option+Y", viewModel.HotkeyText);
        Assert.Equal("Preview: typing and the hotkey only work on macOS", viewModel.Status);
    }
}
