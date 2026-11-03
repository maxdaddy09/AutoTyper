using System.Runtime.InteropServices;
using AutoTyper.Mac.Native;

namespace AutoTyper.Mac;

// `AutoTyper --selftest`: proves on a real Mac that every native function the app imports exists in the
// framework it names, and that the ones used for typing can be called with the declared signatures.
// It opens no window, needs no permission, and posts nothing. Off macOS it does nothing at all.
public static class NativeSelfTest
{
    // Every [DllImport] in AutoTyper.Mac.Native, as (library, entry point). A test keeps this in step
    // with the declarations in both directions.
    public static IReadOnlyList<(string Library, string Symbol)> Symbols { get; } = new (string, string)[]
    {
        (CoreGraphics.Library, "CGEventCreateKeyboardEvent"),
        (CoreGraphics.Library, "CGEventKeyboardSetUnicodeString"),
        (CoreGraphics.Library, "CGEventSetFlags"),
        (CoreGraphics.Library, "CGEventPost"),
        (CoreGraphics.Library, "CGEventSourceFlagsState"),
        (CoreFoundation.Library, "CFRelease"),
        (ApplicationServices.Library, "AXIsProcessTrusted"),
        (Carbon.Library, "GetApplicationEventTarget"),
        (Carbon.Library, "InstallEventHandler"),
        (Carbon.Library, "RemoveEventHandler"),
        (Carbon.Library, "RegisterEventHotKey"),
        (Carbon.Library, "UnregisterEventHotKey"),
    };

    // Writes "selftest ok" and returns 0, or writes one "selftest failed: ..." line per problem and
    // returns 1. Returns before touching any native symbol when this is not macOS.
    public static int Run(TextWriter output)
    {
        if (!OperatingSystem.IsMacOS())
        {
            output.WriteLine("selftest: not macOS");
            return 0;
        }

        var failures = new List<string>();
        CheckSymbolsExist(failures);
        ExerciseTheTypingCalls(failures);

        if (failures.Count == 0)
        {
            output.WriteLine("selftest ok");
            return 0;
        }

        foreach (var failure in failures)
            output.WriteLine($"selftest failed: {failure}");
        return 1;
    }

    // Loads each library by its path and looks each symbol up in it. The lookup does not call anything.
    static void CheckSymbolsExist(List<string> failures)
    {
        foreach (var library in Symbols.Select(s => s.Library).Distinct())
        {
            if (!NativeLibrary.TryLoad(library, out var handle))
            {
                failures.Add($"cannot load {library}");
                continue;
            }

            try
            {
                foreach (var (_, symbol) in Symbols.Where(s => s.Library == library))
                {
                    if (!NativeLibrary.TryGetExport(handle, symbol, out _))
                        failures.Add($"{symbol} is not exported by {library}");
                }
            }
            finally
            {
                NativeLibrary.Free(handle);
            }
        }
    }

    // Calls the functions that are safe to call with nothing at stake. The keyboard event is created,
    // given text and flags the way the key sender does, and released, but never posted. Each step is
    // guarded separately so one failure does not hide the others.
    static void ExerciseTheTypingCalls(List<string> failures)
    {
        Attempt(failures, "creating and releasing a keyboard event", () =>
        {
            var keyEvent = CoreGraphics.CGEventCreateKeyboardEvent(IntPtr.Zero, 0, true);
            if (keyEvent == IntPtr.Zero)
            {
                failures.Add("CGEventCreateKeyboardEvent returned no event");
                return;
            }

            try
            {
                CoreGraphics.CGEventKeyboardSetUnicodeString(keyEvent, 1, new ushort[] { 'a' });
                CoreGraphics.CGEventSetFlags(keyEvent, 0);
            }
            finally
            {
                CoreFoundation.CFRelease(keyEvent);
            }
        });

        // The answers depend on the machine (permission granted or not, which keys are down), so only
        // the fact that the calls come back is checked.
        Attempt(failures, "AXIsProcessTrusted", () => ApplicationServices.AXIsProcessTrusted());
        Attempt(failures, "CGEventSourceFlagsState", () => CoreGraphics.CGEventSourceFlagsState(CoreGraphics.CombinedSessionState));
        Attempt(failures, "GetApplicationEventTarget", () => Carbon.GetApplicationEventTarget());
    }

    static void Attempt(List<string> failures, string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            failures.Add($"{what}: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
