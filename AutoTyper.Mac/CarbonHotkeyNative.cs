using System.Runtime.InteropServices;
using AutoTyper.Mac.Native;

namespace AutoTyper.Mac;

// The real Carbon calls behind ICarbonHotkeyNative, and the only code that makes them for the hotkey.
// Creating one calls nothing; each method calls into macOS only when it is itself called.
internal sealed class CarbonHotkeyNative : ICarbonHotkeyNative
{
    // kEventClassKeyboard ('keyb') and kEventHotKeyPressed.
    internal const uint EventClassKeyboard = 0x6B657962;
    internal const uint EventKindHotKeyPressed = 5;

    // The signature ('ATYP') every hotkey this app registers carries in its EventHotKeyID.
    internal const uint HotKeySignature = 0x41545950;

    // OSStatus handler(EventHandlerCallRef inHandlerCallRef, EventRef inEvent, void *inUserData).
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    delegate int EventHandlerProc(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    // The delegate behind the function pointer given to Carbon. It has to stay reachable for as long as
    // the handler is installed, or the garbage collector frees the thunk Carbon would call.
    EventHandlerProc? handlerProc;

    public bool TryInstallHandler(Func<int> onPressed, out IntPtr handler)
    {
        // Set before the call, so the delegate is held no matter what the runtime does in between.
        var proc = new EventHandlerProc((_, _, _) => Dispatch(onPressed));
        handlerProc = proc;

        var types = new[] { new EventTypeSpec { EventClass = EventClassKeyboard, EventKind = EventKindHotKeyPressed } };
        var status = Carbon.InstallEventHandler(
            Carbon.GetApplicationEventTarget(),
            Marshal.GetFunctionPointerForDelegate(proc),
            (nuint)types.Length,
            types,
            IntPtr.Zero,
            out handler);

        if (status == 0) return true;

        ReportStatus("InstallEventHandler", status);
        handler = IntPtr.Zero;
        handlerProc = null;
        return false;
    }

    public void RemoveHandler(IntPtr handler)
    {
        if (handler == IntPtr.Zero) return;

        // If Carbon refuses to remove it, the handler is still installed and still points at the
        // delegate, so the delegate is kept.
        var status = Carbon.RemoveEventHandler(handler);
        if (status == 0) handlerProc = null;
        else ReportStatus("RemoveEventHandler", status);
    }

    public int RegisterHotkey(uint keyCode, uint carbonModifiers, uint id, out IntPtr hotkey)
    {
        var status = Carbon.RegisterEventHotKey(
            keyCode,
            carbonModifiers,
            new EventHotKeyID { Signature = HotKeySignature, Id = id },
            Carbon.GetApplicationEventTarget(),
            0,
            out hotkey);

        if (status != 0)
        {
            ReportStatus("RegisterEventHotKey", status);
            hotkey = IntPtr.Zero;
        }

        return status;
    }

    public void UnregisterHotkey(IntPtr hotkey)
    {
        if (hotkey == IntPtr.Zero) return;

        var status = Carbon.UnregisterEventHotKey(hotkey);
        if (status != 0) ReportStatus("UnregisterEventHotKey", status);
    }

    // What Carbon's handler runs: the callback's status goes back to Carbon. An exception must not cross
    // into native code, where it would end the process, so it becomes noErr.
    internal static int Dispatch(Func<int> onPressed)
    {
        try
        {
            return onPressed();
        }
        catch (Exception exception)
        {
            Report($"AutoTyper: the hotkey handler threw {exception.GetType().Name}: {exception.Message}");
            return 0;
        }
    }

    // One line on stderr for a Carbon call that did not return noErr (or for a swallowed exception). There
    // is no log file; a hotkey that silently does nothing is otherwise impossible to diagnose, and running
    // the app from Terminal shows these lines. Writing must never fail the caller, and the handler runs
    // inside a native callback, where an exception would end the process.
    static void ReportStatus(string call, int status) =>
        Report($"AutoTyper: {call} returned OSStatus {status}");

    static void Report(string line)
    {
        try
        {
            Console.Error.WriteLine(line);
        }
        catch
        {
        }
    }
}
