namespace AutoTyper;

static class AppPaths
{
    public static string SettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoTyper", "settings.json");
}
