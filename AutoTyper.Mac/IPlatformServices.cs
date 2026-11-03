namespace AutoTyper.Mac;

// One global hotkey that works whichever app has focus.
public interface IGlobalHotkey : IDisposable
{
    // Raised when the registered combination is pressed. The real service raises it on the UI thread.
    event Action? Pressed;

    // The combination that is registered now, or null if none is.
    MacHotkey? Current { get; }

    // Returns false if the combination is invalid or the system refuses it. A refused change must
    // leave the previous hotkey registered and working.
    bool TryRegister(MacHotkey hotkey);
}

// Everything the view model needs from the operating system, so it can run against fakes in tests
// and against a do-nothing implementation where macOS is not available.
public interface IPlatformServices
{
    // True when running somewhere other than macOS: nothing is typed and the hotkey never fires.
    bool IsPreview { get; }

    IKeySender CreateKeySender();

    IGlobalHotkey CreateHotkey();

    // Whether the system lets this app send keystrokes. Cheap enough to ask on every hotkey press.
    bool HasInputPermission();

    // Opens the system page where the user switches the permission on.
    void OpenPermissionSettings();

    // Whether any of these modifier keys is physically held down right now.
    bool AreModifiersDown(MacModifiers modifiers);
}
