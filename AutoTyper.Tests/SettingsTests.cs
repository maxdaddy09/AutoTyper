using System.Text.Json;

namespace AutoTyper.Tests;

public class SettingsTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "AutoTyperTests-" + Guid.NewGuid().ToString("N"));

    public SettingsTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    string PathFor(string name = "settings.json") => Path.Combine(dir, name);

    [Fact]
    public void Defaults_AreCtrlAltT_Letters_50()
    {
        var settings = new Settings();

        Assert.Equal("Ctrl+Alt+T", settings.Hotkey);
        Assert.Equal(TypingMode.Letters, settings.Mode);
        Assert.Equal(50, settings.DelayMs);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var original = new Settings { Hotkey = "Ctrl+Shift+F9", Mode = TypingMode.Words, DelayMs = 1234 };

        Assert.True(original.TrySave(PathFor(), out var error), error);
        var loaded = Settings.Load(PathFor());

        Assert.Equal("Ctrl+Shift+F9", loaded.Hotkey);
        Assert.Equal(TypingMode.Words, loaded.Mode);
        Assert.Equal(1234, loaded.DelayMs);
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var loaded = Settings.Load(PathFor("nope.json"));

        Assert.Equal("Ctrl+Alt+T", loaded.Hotkey);
        Assert.Equal(TypingMode.Letters, loaded.Mode);
        Assert.Equal(50, loaded.DelayMs);
    }

    [Fact]
    public void Load_CorruptJson_ReturnsDefaults()
    {
        File.WriteAllText(PathFor(), "{not json");

        var loaded = Settings.Load(PathFor());

        Assert.Equal("Ctrl+Alt+T", loaded.Hotkey);
        Assert.Equal(50, loaded.DelayMs);
    }

    [Theory]
    [InlineData(99999, 60000)]
    [InlineData(-5, 0)]
    public void Load_ClampsDelay(int stored, int expected)
    {
        File.WriteAllText(PathFor(), $"{{\"DelayMs\": {stored}}}");

        Assert.Equal(expected, Settings.Load(PathFor()).DelayMs);
    }

    [Fact]
    public void TrySave_CreatesMissingDirectory()
    {
        var path = Path.Combine(dir, "sub", "deeper", "settings.json");

        Assert.True(new Settings().TrySave(path, out _));
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void TrySave_UnwritablePath_ReturnsFalseWithMessage()
    {
        var blocker = PathFor("blocker");
        File.WriteAllText(blocker, "");

        var saved = new Settings().TrySave(Path.Combine(blocker, "settings.json"), out var error);

        Assert.False(saved);
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Defaults_WordsPerMinuteIsOffAtSixty()
    {
        var settings = new Settings();

        Assert.False(settings.UseWpm);
        Assert.Equal(60, settings.Wpm);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsWordsPerMinute()
    {
        var original = new Settings { UseWpm = true, Wpm = 90 };

        Assert.True(original.TrySave(PathFor(), out var error), error);
        var loaded = Settings.Load(PathFor());

        Assert.True(loaded.UseWpm);
        Assert.Equal(90, loaded.Wpm);
    }

    [Theory]
    [InlineData(5000, 1000)]
    [InlineData(0, 1)]
    [InlineData(-3, 1)]
    public void Load_ClampsWordsPerMinute(int stored, int expected)
    {
        File.WriteAllText(PathFor(), $"{{\"Wpm\": {stored}}}");

        Assert.Equal(expected, Settings.Load(PathFor()).Wpm);
    }

    [Fact]
    public void Load_FileWrittenBeforeWordsPerMinuteExisted_KeepsItsValuesAndDefaultsTheRest()
    {
        File.WriteAllText(PathFor(), "{\"Hotkey\":\"Ctrl+Shift+F9\",\"Mode\":\"Words\",\"DelayMs\":200}");

        var loaded = Settings.Load(PathFor());

        Assert.Equal("Ctrl+Shift+F9", loaded.Hotkey);
        Assert.Equal(TypingMode.Words, loaded.Mode);
        Assert.Equal(200, loaded.DelayMs);
        Assert.False(loaded.UseWpm);
        Assert.Equal(60, loaded.Wpm);
    }

    [Fact]
    public void SavedJson_ContainsOnlyTheFiveSettings_WithModeAsText()
    {
        new Settings { Mode = TypingMode.Words }.TrySave(PathFor(), out _);

        using var json = JsonDocument.Parse(File.ReadAllText(PathFor()));
        var names = json.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray();

        Assert.Equal(new[] { "DelayMs", "Hotkey", "Mode", "UseWpm", "Wpm" }, names);
        Assert.Equal("Words", json.RootElement.GetProperty("Mode").GetString());
    }
}
