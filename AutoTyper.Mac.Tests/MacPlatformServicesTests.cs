using System.ComponentModel;
using System.Diagnostics;

namespace AutoTyper.Mac.Tests;

// MacPlatformServices is glue around native calls, and nothing native can run on Windows. So these tests
// cover what can be shown without macOS: the choice of settings file, the values it reports, the way
// the Settings page is opened, and that building the object and the things it creates calls nothing
// native. On Windows a native call throws DllNotFoundException, so reaching the assertion is the proof.
// HasInputPermission and AreModifiersDown with a modifier are native calls; `AutoTyper --selftest` on a
// Mac is what exercises them.
public class MacPlatformServicesTests
{
    const string SettingsUrl = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";

    // --- where the settings live ----------------------------------------------------------------

    [Fact]
    public void SettingsPath_IsLibraryApplicationSupportAutoTyperSettingsJson_UnderTheHome()
    {
        var home = Path.Combine(Path.GetTempPath(), "autotyper-fake-home");

        var path = MacPaths.SettingsPath(home);

        // Compared as segments, so it holds with either path separator.
        var relative = Path.GetRelativePath(home, path);
        Assert.Equal(
            new[] { "Library", "Application Support", "AutoTyper", "settings.json" },
            relative.Split(new[] { '/', '\\' }));
        Assert.StartsWith(home, path);
    }

    [Fact]
    public void SettingsPath_DoesNotUseTheDotConfigFolderThatDotNetMapsApplicationDataTo()
    {
        var home = Path.Combine(Path.GetTempPath(), "autotyper-fake-home");

        var segments = MacPaths.SettingsPath(home).Split(new[] { '/', '\\' });

        Assert.DoesNotContain(".config", segments);
    }

    [Fact]
    public void SettingsPath_AHomeWithATrailingSeparatorGivesTheSameFile()
    {
        var home = Path.Combine(Path.GetTempPath(), "autotyper-fake-home");

        var withSeparator = MacPaths.SettingsPath(home + Path.DirectorySeparatorChar);

        Assert.Equal(Path.GetFullPath(MacPaths.SettingsPath(home)), Path.GetFullPath(withSeparator));
    }

    [Fact]
    public void SettingsPath_OnlyBuildsTheNameAndCreatesNothing()
    {
        var home = Path.Combine(Path.GetTempPath(), "autotyper-fake-home-" + Guid.NewGuid().ToString("N"));

        var path = MacPaths.SettingsPath(home);

        Assert.False(Directory.Exists(home));
        Assert.False(File.Exists(path));
    }

    // --- what it reports ------------------------------------------------------------------------

    [Fact]
    public void ConstructingIt_CallsNothingNative()
    {
        IPlatformServices platform = new MacPlatformServices();

        Assert.NotNull(platform);
    }

    [Fact]
    public void IsPreview_IsFalse()
    {
        Assert.False(new MacPlatformServices().IsPreview);
    }

    [Fact]
    public void CreateKeySender_GivesTheMacKeySender_WithoutCallingNativeCode()
    {
        var sender = new MacPlatformServices().CreateKeySender();

        Assert.IsType<MacKeySender>(sender);
    }

    [Fact]
    public void CreateHotkey_GivesAMacHotkeyService_WithNothingRegistered()
    {
        // Creating it, and letting it go again, registers nothing and so calls nothing native.
        using var hotkey = new MacPlatformServices().CreateHotkey();

        Assert.IsType<MacHotkeyService>(hotkey);
        Assert.Null(hotkey.Current);
    }

    [Fact]
    public void EachCreateHotkeyCall_GivesItsOwnHotkey()
    {
        var platform = new MacPlatformServices();
        using var one = platform.CreateHotkey();
        using var two = platform.CreateHotkey();

        Assert.NotSame(one, two);
    }

    // --- modifiers ------------------------------------------------------------------------------

    [Theory]
    [InlineData(MacModifiers.None)]
    [InlineData((MacModifiers)16)]    // no modifier we know: the mask is empty, so there is nothing to ask
    [InlineData((MacModifiers)0x70)]
    public void AreModifiersDown_NoKnownModifier_IsFalseWithoutAskingTheSystem(MacModifiers modifiers)
    {
        // The system call would throw here, so a plain false means it was never made.
        Assert.False(new MacPlatformServices().AreModifiersDown(modifiers));
    }

    // --- opening the Settings page --------------------------------------------------------------

    [Fact]
    public void OpenPermissionSettings_RunsOpenWithTheAccessibilityPrivacyUrl_WithoutAShell()
    {
        var started = new List<ProcessStartInfo>();
        var platform = new MacPlatformServices(started.Add);

        platform.OpenPermissionSettings();

        var start = Assert.Single(started);
        // The full path: .NET on Unix may look a bare name up in the app's own folder and the current
        // folder before PATH, and "open" must be the system tool, not a file that happens to sit there.
        Assert.Equal("/usr/bin/open", start.FileName);
        Assert.Equal(SettingsUrl, start.Arguments);
        Assert.False(start.UseShellExecute);
    }

    [Fact]
    public void OpenPermissionSettings_NeverThrows_WhateverGoesWrongStartingOpen()
    {
        var failures = new Exception[]
        {
            new Win32Exception(2, "No such file or directory"),
            new InvalidOperationException("no file name"),
            new PlatformNotSupportedException(),
            new UnauthorizedAccessException(),
        };

        foreach (var failure in failures)
        {
            var platform = new MacPlatformServices(_ => throw failure);

            var exception = Record.Exception(platform.OpenPermissionSettings);

            Assert.True(exception is null, $"{failure.GetType().Name} escaped: {exception}");
        }
    }
}
