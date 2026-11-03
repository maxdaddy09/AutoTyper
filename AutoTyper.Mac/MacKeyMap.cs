using Avalonia.Input;

namespace AutoTyper.Mac;

public static class MacKeyMap
{
    // Takes the physical key rather than Avalonia's layout-dependent Key: the hotkey is registered
    // by virtual key code, which is a position on the keyboard, so it has to be stored from the same
    // position the user pressed, whatever layout is active. Returns null for keys a hotkey cannot use.
    public static MacKey? FromAvalonia(PhysicalKey key) => key switch
    {
        PhysicalKey.A => MacKey.A,
        PhysicalKey.B => MacKey.B,
        PhysicalKey.C => MacKey.C,
        PhysicalKey.D => MacKey.D,
        PhysicalKey.E => MacKey.E,
        PhysicalKey.F => MacKey.F,
        PhysicalKey.G => MacKey.G,
        PhysicalKey.H => MacKey.H,
        PhysicalKey.I => MacKey.I,
        PhysicalKey.J => MacKey.J,
        PhysicalKey.K => MacKey.K,
        PhysicalKey.L => MacKey.L,
        PhysicalKey.M => MacKey.M,
        PhysicalKey.N => MacKey.N,
        PhysicalKey.O => MacKey.O,
        PhysicalKey.P => MacKey.P,
        PhysicalKey.Q => MacKey.Q,
        PhysicalKey.R => MacKey.R,
        PhysicalKey.S => MacKey.S,
        PhysicalKey.T => MacKey.T,
        PhysicalKey.U => MacKey.U,
        PhysicalKey.V => MacKey.V,
        PhysicalKey.W => MacKey.W,
        PhysicalKey.X => MacKey.X,
        PhysicalKey.Y => MacKey.Y,
        PhysicalKey.Z => MacKey.Z,
        PhysicalKey.Digit0 => MacKey.D0,
        PhysicalKey.Digit1 => MacKey.D1,
        PhysicalKey.Digit2 => MacKey.D2,
        PhysicalKey.Digit3 => MacKey.D3,
        PhysicalKey.Digit4 => MacKey.D4,
        PhysicalKey.Digit5 => MacKey.D5,
        PhysicalKey.Digit6 => MacKey.D6,
        PhysicalKey.Digit7 => MacKey.D7,
        PhysicalKey.Digit8 => MacKey.D8,
        PhysicalKey.Digit9 => MacKey.D9,
        PhysicalKey.F1 => MacKey.F1,
        PhysicalKey.F2 => MacKey.F2,
        PhysicalKey.F3 => MacKey.F3,
        PhysicalKey.F4 => MacKey.F4,
        PhysicalKey.F5 => MacKey.F5,
        PhysicalKey.F6 => MacKey.F6,
        PhysicalKey.F7 => MacKey.F7,
        PhysicalKey.F8 => MacKey.F8,
        PhysicalKey.F9 => MacKey.F9,
        PhysicalKey.F10 => MacKey.F10,
        PhysicalKey.F11 => MacKey.F11,
        PhysicalKey.F12 => MacKey.F12,
        PhysicalKey.Space => MacKey.Space,
        _ => null,
    };
}
