using System.Runtime.InteropServices;

namespace AutoTyper.Mac.Native;

// struct EventTypeSpec { OSType eventClass; UInt32 eventKind; }
[StructLayout(LayoutKind.Sequential)]
internal struct EventTypeSpec
{
    public uint EventClass;
    public uint EventKind;
}

// struct EventHotKeyID { OSType signature; UInt32 id; }, passed to RegisterEventHotKey by value.
[StructLayout(LayoutKind.Sequential)]
internal struct EventHotKeyID
{
    public uint Signature;
    public uint Id;
}

// The Carbon Event Manager (CarbonEventsCore.h, CarbonEvents.h), still the way to register a global hotkey
// that needs no Accessibility permission.
internal static class Carbon
{
    public const string Library = "/System/Library/Frameworks/Carbon.framework/Carbon";

    // EventTargetRef GetApplicationEventTarget(void);
    [DllImport(Library)]
    public static extern IntPtr GetApplicationEventTarget();

    // OSStatus InstallEventHandler(EventTargetRef inTarget, EventHandlerUPP inHandler, ItemCount inNumTypes,
    //                              const EventTypeSpec *inList, void *inUserData, EventHandlerRef *outRef);
    // The handler is a plain C function pointer (OSStatus handler(EventHandlerCallRef, EventRef, void *)).
    // ItemCount is 'typedef unsigned long' in MacTypes.h: 64 bits on macOS, so numTypes is nuint.
    [DllImport(Library)]
    public static extern int InstallEventHandler(
        IntPtr target,
        IntPtr handler,
        nuint numTypes,
        EventTypeSpec[] types,
        IntPtr userData,
        out IntPtr handlerRef);

    // OSStatus RemoveEventHandler(EventHandlerRef inHandlerRef);
    [DllImport(Library)]
    public static extern int RemoveEventHandler(IntPtr handlerRef);

    // OSStatus RegisterEventHotKey(UInt32 inHotKeyCode, UInt32 inHotKeyModifiers, EventHotKeyID inHotKeyID,
    //                              EventTargetRef inTarget, OptionBits inOptions, EventHotKeyRef *outRef);
    [DllImport(Library)]
    public static extern int RegisterEventHotKey(
        uint keyCode,
        uint modifiers,
        EventHotKeyID id,
        IntPtr target,
        uint options,
        out IntPtr hotKeyRef);

    // OSStatus UnregisterEventHotKey(EventHotKeyRef inHotKey);
    [DllImport(Library)]
    public static extern int UnregisterEventHotKey(IntPtr hotKeyRef);
}
