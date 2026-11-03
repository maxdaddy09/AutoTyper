namespace AutoTyper.Mac;

// The global hotkey on macOS, through the Carbon Event Manager's RegisterEventHotKey: it needs no
// Accessibility permission and fires whichever app has focus.
//
// One application event handler is installed on first use. Carbon calls it on the main thread when a
// registered hotkey is pressed; it only posts the work to the UI thread and returns at once.
//
// Used from the UI thread, like the view model that owns it.
public sealed class MacHotkeyService : IGlobalHotkey
{
    // Each registration is told apart by this id; a change registers the new combination under the id
    // the current one does not use, so the two never clash while both exist.
    const uint FirstId = 1;
    const uint SecondId = 2;

    readonly Action<Action> postToUiThread;
    readonly ICarbonHotkeyNative native;

    bool handlerInstalled;
    IntPtr handler;

    IntPtr registration;
    uint registrationId;   // 0 while nothing is registered
    bool disposed;

    // postToUiThread runs the action it is given on the UI thread (Dispatcher.UIThread.Post in the app).
    public MacHotkeyService(Action<Action> postToUiThread)
        : this(postToUiThread, new CarbonHotkeyNative())
    {
    }

    internal MacHotkeyService(Action<Action> postToUiThread, ICarbonHotkeyNative native)
    {
        ArgumentNullException.ThrowIfNull(postToUiThread);
        ArgumentNullException.ThrowIfNull(native);
        this.postToUiThread = postToUiThread;
        this.native = native;
    }

    public event Action? Pressed;

    public MacHotkey? Current { get; private set; }

    public bool TryRegister(MacHotkey hotkey)
    {
        if (disposed || !hotkey.IsValid) return false;
        if (hotkey == Current) return true;
        if (!EnsureHandlerInstalled()) return false;

        // The new combination goes in first. Only once the system has accepted it is the old one let go,
        // so a refused change leaves the previous hotkey registered and working.
        var id = registrationId == FirstId ? SecondId : FirstId;
        var status = native.RegisterHotkey(hotkey.KeyCode, hotkey.CarbonModifiers, id, out var newRegistration);
        if (status != 0) return false;

        if (Current != null) native.UnregisterHotkey(registration);
        registration = newRegistration;
        registrationId = id;
        Current = hotkey;
        return true;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;

        if (Current != null)
        {
            native.UnregisterHotkey(registration);
            registration = IntPtr.Zero;
            registrationId = 0;
            Current = null;
        }

        if (handlerInstalled)
        {
            native.RemoveHandler(handler);
            handler = IntPtr.Zero;
            handlerInstalled = false;
        }
    }

    bool EnsureHandlerInstalled()
    {
        if (handlerInstalled) return true;
        if (!native.TryInstallHandler(OnCarbonPress, out handler)) return false;

        handlerInstalled = true;
        return true;
    }

    // Runs inside Carbon's event dispatch: nothing but handing the work over, then noErr.
    int OnCarbonPress()
    {
        postToUiThread(RaisePressed);
        return 0;
    }

    void RaisePressed()
    {
        // A press posted just before Dispose must not reach anyone afterwards.
        if (!disposed) Pressed?.Invoke();
    }
}
