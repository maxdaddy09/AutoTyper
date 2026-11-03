using System.Diagnostics;
using Avalonia.Threading;
using AutoTyper.Mac.Native;

namespace AutoTyper.Mac;

// The real thing on macOS. Program only builds it when OperatingSystem.IsMacOS(): every method that
// reaches into the system does so only when it is called, and constructing the object, or creating the
// key sender or the hotkey, calls nothing native.
//
// The view model calls AreModifiersDown and uses the key sender from a thread-pool thread. Neither
// keeps state, so that is safe.
public sealed class MacPlatformServices : IPlatformServices
{
    const string AccessibilitySettingsUrl =
        "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility";

    readonly Action<ProcessStartInfo> startProcess;

    public MacPlatformServices()
        : this(start => Process.Start(start)?.Dispose())
    {
    }

    // The way "open" is started is passed in so a test can see what would be run without running it.
    internal MacPlatformServices(Action<ProcessStartInfo> startProcess)
    {
        ArgumentNullException.ThrowIfNull(startProcess);
        this.startProcess = startProcess;
    }

    public bool IsPreview => false;

    public IKeySender CreateKeySender() => new MacKeySender();

    // The press is raised on the UI thread: Carbon calls back on the main thread, and the service hands
    // the work to the dispatcher. A lambda, because Post's optional priority stops it being an Action<Action>.
    public IGlobalHotkey CreateHotkey() => new MacHotkeyService(action => Dispatcher.UIThread.Post(action));

    public bool HasInputPermission() => ApplicationServices.AXIsProcessTrusted();

    // Failing to open the Settings page must not take the app down, so nothing escapes. The window keeps
    // showing the permission text and the button, and the user can open System Settings by hand.
    public void OpenPermissionSettings()
    {
        try
        {
            startProcess(new ProcessStartInfo("open", AccessibilitySettingsUrl) { UseShellExecute = false });
        }
        catch (Exception)
        {
        }
    }

    public bool AreModifiersDown(MacModifiers modifiers)
    {
        // No modifier asked about means nothing can be down, and there is nothing to ask the system.
        var mask = modifiers.ToEventFlagMask();
        if (mask == 0) return false;

        return (CoreGraphics.CGEventSourceFlagsState(CoreGraphics.CombinedSessionState) & mask) != 0;
    }
}
