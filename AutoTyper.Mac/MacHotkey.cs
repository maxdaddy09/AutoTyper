namespace AutoTyper.Mac;

// The app's own flags; ToCarbonModifiers and ToEventFlagMask translate them to what macOS expects.
[Flags]
public enum MacModifiers { None = 0, Control = 1, Option = 2, Shift = 4, Command = 8 }

public static class MacModifiersExtensions
{
    // The values RegisterEventHotKey takes: cmdKey, shiftKey, optionKey and controlKey.
    public static uint ToCarbonModifiers(this MacModifiers modifiers)
    {
        uint carbon = 0;
        if (modifiers.HasFlag(MacModifiers.Command)) carbon |= 0x100;
        if (modifiers.HasFlag(MacModifiers.Shift)) carbon |= 0x200;
        if (modifiers.HasFlag(MacModifiers.Option)) carbon |= 0x800;
        if (modifiers.HasFlag(MacModifiers.Control)) carbon |= 0x1000;
        return carbon;
    }

    // The CGEventFlags masks for the same modifiers: shift, control, alternate and command.
    public static ulong ToEventFlagMask(this MacModifiers modifiers)
    {
        ulong mask = 0;
        if (modifiers.HasFlag(MacModifiers.Shift)) mask |= 0x20000;
        if (modifiers.HasFlag(MacModifiers.Control)) mask |= 0x40000;
        if (modifiers.HasFlag(MacModifiers.Option)) mask |= 0x80000;
        if (modifiers.HasFlag(MacModifiers.Command)) mask |= 0x100000;
        return mask;
    }
}

// Each value is the macOS virtual key code (kVK_ANSI_*, kVK_Space, kVK_F*): a position on the
// keyboard, not a character, so the same key means the same thing on any keyboard layout.
public enum MacKey : ushort
{
    A = 0x00, S = 0x01, D = 0x02, F = 0x03, H = 0x04, G = 0x05, Z = 0x06, X = 0x07,
    C = 0x08, V = 0x09, B = 0x0B, Q = 0x0C, W = 0x0D, E = 0x0E, R = 0x0F, Y = 0x10,
    T = 0x11,
    D1 = 0x12, D2 = 0x13, D3 = 0x14, D4 = 0x15, D6 = 0x16, D5 = 0x17, D9 = 0x19, D7 = 0x1A,
    D8 = 0x1C, D0 = 0x1D,
    O = 0x1F, U = 0x20, I = 0x22, P = 0x23, L = 0x25, J = 0x26, K = 0x28, N = 0x2D, M = 0x2E,
    Space = 0x31,
    F1 = 0x7A, F2 = 0x78, F3 = 0x63, F4 = 0x76, F5 = 0x60, F6 = 0x61, F7 = 0x62, F8 = 0x64,
    F9 = 0x65, F10 = 0x6D, F11 = 0x67, F12 = 0x6F,
}

public sealed record MacHotkey(MacModifiers Modifiers, MacKey Key)
{
    const MacModifiers AllModifiers =
        MacModifiers.Control | MacModifiers.Option | MacModifiers.Shift | MacModifiers.Command;

    public static readonly MacHotkey Default = new(MacModifiers.Control | MacModifiers.Option, MacKey.T);

    // Digits are written as the single character on the key, so D1 is "1".
    static readonly Dictionary<string, MacKey> KeysByName =
        Enum.GetValues<MacKey>().ToDictionary(KeyName, key => key, StringComparer.OrdinalIgnoreCase);

    public bool IsValid =>
        Enum.IsDefined(Key)
        && (Modifiers & ~AllModifiers) == 0
        && (Modifiers != MacModifiers.None || IsFunctionKey(Key));

    public uint KeyCode => (uint)Key;

    public uint CarbonModifiers => Modifiers.ToCarbonModifiers();

    public ulong EventFlagMask => Modifiers.ToEventFlagMask();

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(MacModifiers.Control)) parts.Add("Control");
        if (Modifiers.HasFlag(MacModifiers.Option)) parts.Add("Option");
        if (Modifiers.HasFlag(MacModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(MacModifiers.Command)) parts.Add("Command");
        parts.Add(KeyName(Key));
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out MacHotkey hotkey)
    {
        hotkey = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = MacModifiers.None;
        foreach (var token in tokens[..^1])
        {
            switch (token.ToLowerInvariant())
            {
                case "control": modifiers |= MacModifiers.Control; break;
                case "option": modifiers |= MacModifiers.Option; break;
                case "shift": modifiers |= MacModifiers.Shift; break;
                case "command": modifiers |= MacModifiers.Command; break;
                default: return false;
            }
        }

        if (!KeysByName.TryGetValue(tokens[^1], out var key)) return false;
        var candidate = new MacHotkey(modifiers, key);
        if (!candidate.IsValid) return false;

        hotkey = candidate;
        return true;
    }

    static bool IsFunctionKey(MacKey key) =>
        key is MacKey.F1 or MacKey.F2 or MacKey.F3 or MacKey.F4 or MacKey.F5 or MacKey.F6
            or MacKey.F7 or MacKey.F8 or MacKey.F9 or MacKey.F10 or MacKey.F11 or MacKey.F12;

    static string KeyName(MacKey key)
    {
        var name = key.ToString();
        return name.Length == 2 && name[0] == 'D' && char.IsAsciiDigit(name[1]) ? name[1..] : name;
    }
}
