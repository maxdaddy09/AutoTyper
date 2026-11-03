namespace AutoTyper;

// Owns a hidden message-only window that receives WM_HOTKEY, so the hotkey works
// whichever window has focus and does not depend on MainForm's handle.
public sealed class HotkeyRegistrar : NativeWindow, IDisposable
{
    const int WmHotkey = 0x0312;
    static readonly IntPtr HwndMessage = new(-3);

    int currentId;

    public HotkeyRegistrar()
    {
        CreateHandle(new CreateParams { Parent = HwndMessage });
    }

    public event Action? Pressed;

    public HotkeyBinding? Current { get; private set; }

    // The new combination is registered under the other id first and the old one released
    // afterwards, so a failed change leaves the previous hotkey working.
    public bool TryRegister(HotkeyBinding binding)
    {
        if (!binding.IsValid) return false;
        if (binding == Current) return true;

        var newId = currentId == 1 ? 2 : 1;
        if (!NativeMethods.RegisterHotKey(Handle, newId, binding.NativeModifiers, binding.NativeKey))
            return false;

        if (currentId != 0) NativeMethods.UnregisterHotKey(Handle, currentId);
        currentId = newId;
        Current = binding;
        return true;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && (int)m.WParam == currentId)
            Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (Handle == IntPtr.Zero) return;
        if (currentId != 0) NativeMethods.UnregisterHotKey(Handle, currentId);
        currentId = 0;
        Current = null;
        DestroyHandle();
    }
}
