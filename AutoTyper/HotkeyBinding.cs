namespace AutoTyper;

// Values match the Win32 MOD_* flags so they can be passed to RegisterHotKey as they are.
[Flags]
public enum HotkeyModifiers { None = 0, Alt = 1, Control = 2, Shift = 4, Win = 8 }

public sealed record HotkeyBinding(HotkeyModifiers Modifiers, Keys Key)
{
    const uint ModNoRepeat = 0x4000;

    static readonly Keys[] ModifierKeys =
    {
        Keys.ControlKey, Keys.LControlKey, Keys.RControlKey,
        Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey,
        Keys.Menu, Keys.LMenu, Keys.RMenu,
        Keys.LWin, Keys.RWin,
    };

    public static readonly HotkeyBinding Default =
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt, Keys.T);

    public bool IsValid =>
        Key != Keys.None
        && (Key & Keys.KeyCode) == Key
        && !ModifierKeys.Contains(Key)
        && (Modifiers != HotkeyModifiers.None || (Key >= Keys.F1 && Key <= Keys.F24));

    // Without NOREPEAT, holding the combination down fires it repeatedly and a start would be
    // followed straight away by a stop.
    public uint NativeModifiers => (uint)Modifiers | ModNoRepeat;

    public uint NativeKey => (uint)Key;

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key is >= Keys.D0 and <= Keys.D9 ? ((int)Key - (int)Keys.D0).ToString() : Key.ToString());
        return string.Join('+', parts);
    }

    public static bool TryParse(string? text, out HotkeyBinding binding)
    {
        binding = Default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var tokens = text.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = HotkeyModifiers.None;
        foreach (var token in tokens[..^1])
        {
            switch (token.ToLowerInvariant())
            {
                case "ctrl": modifiers |= HotkeyModifiers.Control; break;
                case "alt": modifiers |= HotkeyModifiers.Alt; break;
                case "shift": modifiers |= HotkeyModifiers.Shift; break;
                case "win": modifiers |= HotkeyModifiers.Win; break;
                default: return false;
            }
        }

        if (!TryParseKey(tokens[^1], out var key)) return false;
        var candidate = new HotkeyBinding(modifiers, key);
        if (!candidate.IsValid) return false;

        binding = candidate;
        return true;
    }

    static bool TryParseKey(string token, out Keys key)
    {
        key = Keys.None;
        if (token.Length == 1 && char.IsAsciiDigit(token[0]))
        {
            key = Keys.D0 + (token[0] - '0');
            return true;
        }

        // Enum.TryParse also accepts numbers and comma-separated flag lists; only names are wanted.
        if (token.Length == 0 || !char.IsLetter(token[0]) || token.Contains(',')) return false;
        return Enum.TryParse(token, ignoreCase: true, out key);
    }
}
