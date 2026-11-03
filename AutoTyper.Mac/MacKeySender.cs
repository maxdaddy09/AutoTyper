using AutoTyper.Mac.Native;

namespace AutoTyper.Mac;

// Types by posting keyboard CGEvents to the HID event tap, the same stream the hardware feeds, so it
// reaches whichever app has focus. macOS drops these events silently unless the app has the
// Accessibility permission, so the caller checks the permission first (HasInputPermission).
//
// Text goes out as Unicode strings on key events with virtual key 0, so the layout and the keys the
// user is holding do not matter. Every event has its flags cleared first: a modifier still held from
// the hotkey would otherwise turn the text into shortcuts.
public sealed class MacKeySender : IKeySender
{
    // kVK_Return and kVK_Tab.
    const ushort ReturnKey = 0x24;
    const ushort TabKey = 0x30;

    // CGEventKeyboardSetUnicodeString takes at most 20 UTF-16 code units per event.
    const int MaxUnitsPerEvent = 20;

    // Splits the text into groups of at most maxUnits UTF-16 code units, in order, without ever
    // separating the two halves of a surrogate pair (a group can then be one unit shorter).
    public static IReadOnlyList<string> SplitForEvents(string text, int maxUnits = MaxUnitsPerEvent)
    {
        ArgumentNullException.ThrowIfNull(text);
        // A pair is two units, so a smaller limit could not keep one whole.
        ArgumentOutOfRangeException.ThrowIfLessThan(maxUnits, 2);

        var groups = new List<string>();
        var start = 0;
        while (start < text.Length)
        {
            var end = Math.Min(start + maxUnits, text.Length);
            if (end < text.Length && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end]))
                end--;

            groups.Add(text.Substring(start, end - start));
            start = end;
        }

        return groups;
    }

    public bool SendText(string text)
    {
        foreach (var group in SplitForEvents(text))
        {
            if (!PostKey(0, group)) return false;
        }

        return true;
    }

    public bool SendEnter() => PostKey(ReturnKey, null);

    public bool SendTab() => PostKey(TabKey, null);

    // Posts a key-down and then a key-up. Both events are created before either is posted, so if one
    // cannot be created nothing is sent and no key is left held down. Returns false only in that case.
    static bool PostKey(ushort virtualKey, string? unicode)
    {
        var down = IntPtr.Zero;
        var up = IntPtr.Zero;
        try
        {
            down = CreateEvent(virtualKey, true, unicode);
            up = CreateEvent(virtualKey, false, unicode);
            if (down == IntPtr.Zero || up == IntPtr.Zero) return false;

            CoreGraphics.CGEventPost(CoreGraphics.HidEventTap, down);
            CoreGraphics.CGEventPost(CoreGraphics.HidEventTap, up);
            return true;
        }
        finally
        {
            if (down != IntPtr.Zero) CoreFoundation.CFRelease(down);
            if (up != IntPtr.Zero) CoreFoundation.CFRelease(up);
        }
    }

    // Returns IntPtr.Zero if macOS could not create the event.
    static IntPtr CreateEvent(ushort virtualKey, bool keyDown, string? unicode)
    {
        var keyEvent = CoreGraphics.CGEventCreateKeyboardEvent(IntPtr.Zero, virtualKey, keyDown);
        if (keyEvent == IntPtr.Zero) return IntPtr.Zero;

        if (unicode != null)
        {
            var units = new ushort[unicode.Length];
            for (var i = 0; i < units.Length; i++) units[i] = unicode[i];
            CoreGraphics.CGEventKeyboardSetUnicodeString(keyEvent, (nuint)units.Length, units);
        }

        CoreGraphics.CGEventSetFlags(keyEvent, 0);
        return keyEvent;
    }
}
