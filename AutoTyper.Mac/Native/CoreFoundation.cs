using System.Runtime.InteropServices;

namespace AutoTyper.Mac.Native;

internal static class CoreFoundation
{
    public const string Library = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // void CFRelease(CFTypeRef cf);   Must not be given NULL.
    [DllImport(Library)]
    public static extern void CFRelease(IntPtr cf);
}
