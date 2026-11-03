namespace AutoTyper.Mac;

// Where the macOS app keeps its files. Pure string work, so it can be checked on any platform.
internal static class MacPaths
{
    // ~/Library/Application Support/AutoTyper/settings.json, where macOS apps keep their data. Built from
    // the home folder, not from SpecialFolder.ApplicationData, which .NET maps to ~/.config on macOS.
    internal static string SettingsPath(string home) =>
        Path.Combine(home, "Library", "Application Support", "AutoTyper", "settings.json");
}
