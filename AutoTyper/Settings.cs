using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoTyper;

// Only the hotkey, mode and delay are stored. The text being typed may be a password,
// so it is deliberately not part of this class.
public sealed class Settings
{
    public const int MaxDelayMs = 60000;
    public const int MinWpm = 1;
    public const int MaxWpm = 1000;

    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public string Hotkey { get; set; } = HotkeyBinding.Default.ToString();
    public TypingMode Mode { get; set; } = TypingMode.Letters;
    public int DelayMs { get; set; } = 50;

    // Words mode only: pace by words per minute instead of a delay after each word.
    public bool UseWpm { get; set; }
    public int Wpm { get; set; } = 60;

    public static string DefaultPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AutoTyper", "settings.json");

    public static Settings Load(string path)
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), JsonOptions) ?? new Settings();
            settings.DelayMs = Math.Clamp(settings.DelayMs, 0, MaxDelayMs);
            settings.Wpm = Math.Clamp(settings.Wpm, MinWpm, MaxWpm);
            return settings;
        }
        catch (Exception)
        {
            return new Settings();
        }
    }

    public bool TrySave(string path, out string? error)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
