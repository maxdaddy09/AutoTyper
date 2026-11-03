using System.Runtime.InteropServices;

namespace AutoTyper.Mac.Native;

internal static class ApplicationServices
{
    public const string Library = "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";

    // Boolean AXIsProcessTrusted(void);   (HIServices, re-exported by ApplicationServices)
    // Boolean is an unsigned char: one byte, not the four a default .NET bool would be.
    [DllImport(Library)]
    [return: MarshalAs(UnmanagedType.U1)]
    public static extern bool AXIsProcessTrusted();
}
