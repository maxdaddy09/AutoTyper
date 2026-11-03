using System.Runtime.InteropServices;

namespace AutoTyper;

public sealed class SendInputKeySender : IKeySender
{
    const uint InputKeyboard = 1;
    const uint KeyEventFKeyUp = 0x0002;
    const uint KeyEventFUnicode = 0x0004;
    const ushort VkTab = 0x09;
    const ushort VkReturn = 0x0D;

    // A single word can be very long; cap how much goes into one SendInput call.
    const int MaxCharsPerCall = 512;

    public bool SendText(string text)
    {
        for (var start = 0; start < text.Length; start += MaxCharsPerCall)
        {
            var count = Math.Min(MaxCharsPerCall, text.Length - start);
            var inputs = new NativeMethods.INPUT[count * 2];
            for (var i = 0; i < count; i++)
            {
                inputs[i * 2] = Key(0, text[start + i], KeyEventFUnicode);
                inputs[i * 2 + 1] = Key(0, text[start + i], KeyEventFUnicode | KeyEventFKeyUp);
            }

            if (!Send(inputs)) return false;
        }

        return true;
    }

    public bool SendEnter() => SendVirtualKey(VkReturn);

    public bool SendTab() => SendVirtualKey(VkTab);

    static bool SendVirtualKey(ushort virtualKey) =>
        Send(new[] { Key(virtualKey, 0, 0), Key(virtualKey, 0, KeyEventFKeyUp) });

    static bool Send(NativeMethods.INPUT[] inputs) =>
        NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>()) == inputs.Length;

    static NativeMethods.INPUT Key(ushort virtualKey, ushort scanCode, uint flags) => new()
    {
        type = InputKeyboard,
        u = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT { wVk = virtualKey, wScan = scanCode, dwFlags = flags },
        },
    };
}
