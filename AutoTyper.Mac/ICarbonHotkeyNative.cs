namespace AutoTyper.Mac;

// The few Carbon operations MacHotkeyService needs, so its registration logic can run against a fake
// off macOS. CarbonHotkeyNative is the real one. Status values are Carbon OSStatus codes: 0 is noErr.
internal interface ICarbonHotkeyNative
{
    // Installs the application-wide handler for "a registered hotkey was pressed". onPressed is called
    // on the thread Carbon delivers the event on (the main thread) and its result goes back to Carbon as
    // the handler's OSStatus. Returns false if the handler could not be installed; handler identifies
    // an installed one for RemoveHandler.
    bool TryInstallHandler(Func<int> onPressed, out IntPtr handler);

    void RemoveHandler(IntPtr handler);

    // Registers a hotkey with the system and returns the OSStatus. On 0, hotkey identifies the
    // registration for UnregisterHotkey; on anything else nothing is registered.
    int RegisterHotkey(uint keyCode, uint carbonModifiers, uint id, out IntPtr hotkey);

    void UnregisterHotkey(IntPtr hotkey);
}
