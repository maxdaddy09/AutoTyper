using System.Runtime.InteropServices;

namespace AutoTyper.Mac.Native;

// Quartz Event Services (CGEvent.h, CGEventSource.h). Nothing here runs unless a method is called, and
// only the macOS key sender and the self-test call them.
internal static class CoreGraphics
{
    public const string Library = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    // The CGEventTapLocation for kCGHIDEventTap: events enter the system as if from the keyboard hardware.
    public const uint HidEventTap = 0;

    // The CGEventSourceStateID for kCGEventSourceStateCombinedSessionState.
    public const int CombinedSessionState = 0;

    // CGEventRef CGEventCreateKeyboardEvent(CGEventSourceRef source, CGKeyCode virtualKey, bool keyDown);
    // Returns NULL if the event could not be created. The caller owns the result and must CFRelease it.
    [DllImport(Library)]
    public static extern IntPtr CGEventCreateKeyboardEvent(
        IntPtr source,
        ushort virtualKey,
        [MarshalAs(UnmanagedType.U1)] bool keyDown);

    // void CGEventKeyboardSetUnicodeString(CGEventRef event, UniCharCount stringLength, const UniChar *unicodeString);
    // UniCharCount is unsigned long and UniChar is a 16-bit unit, which is nuint and ushort here.
    [DllImport(Library)]
    public static extern void CGEventKeyboardSetUnicodeString(IntPtr @event, nuint length, ushort[] chars);

    // void CGEventSetFlags(CGEventRef event, CGEventFlags flags);   CGEventFlags is a 64-bit mask.
    [DllImport(Library)]
    public static extern void CGEventSetFlags(IntPtr @event, ulong flags);

    // void CGEventPost(CGEventTapLocation tap, CGEventRef event);
    [DllImport(Library)]
    public static extern void CGEventPost(uint tapLocation, IntPtr @event);

    // CGEventFlags CGEventSourceFlagsState(CGEventSourceStateID stateID);   CGEventSourceStateID is a 32-bit int.
    [DllImport(Library)]
    public static extern ulong CGEventSourceFlagsState(int stateId);
}
