using Avalonia;

namespace AutoTyper.Mac;

internal static class Program
{
    // The preview keeps its own settings, so running it on Windows never touches the real apps' settings.
    static string PreviewSettingsPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AutoTyper-Mac-Preview",
        "settings.json");

    [STAThread]
    public static int Main(string[] args)
    {
        // The self-test runs before Avalonia starts: it opens no window and needs no permission.
        if (args.Contains("--selftest")) return NativeSelfTest.Run(Console.Out);

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect();

    // The one place that chooses the platform and the settings file. The caller subscribes to the
    // view model and then calls Start().
    internal static MainViewModel CreateViewModel(Func<bool> isWindowActive)
    {
        var settingsPath = PreviewSettingsPath;
        return new MainViewModel(new PreviewPlatformServices(), Settings.Load(settingsPath), settingsPath, isWindowActive);
    }
}
