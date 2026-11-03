namespace AutoTyper.Mac;

// Used where macOS is not available (this is how the window is run and checked on Windows): nothing is
// typed, the hotkey is accepted but never fires, and the permission is always granted.
public sealed class PreviewPlatformServices : IPlatformServices
{
    public bool IsPreview => true;

    public IKeySender CreateKeySender() => new RecordingKeySender();

    public IGlobalHotkey CreateHotkey() => new PreviewHotkey();

    public bool HasInputPermission() => true;

    public void OpenPermissionSettings()
    {
    }

    public bool AreModifiersDown(MacModifiers modifiers) => false;

    // Keeps what it was asked to type, in the order it was asked ("T:" text, "Enter", "Tab").
    // Called from the job's thread-pool thread, so the list is guarded.
    public sealed class RecordingKeySender : IKeySender
    {
        readonly object gate = new();
        readonly List<string> sent = new();

        public IReadOnlyList<string> Sent
        {
            get
            {
                lock (gate) return sent.ToArray();
            }
        }

        public bool SendText(string text) => Record($"T:{text}");

        public bool SendEnter() => Record("Enter");

        public bool SendTab() => Record("Tab");

        bool Record(string call)
        {
            lock (gate) sent.Add(call);
            return true;
        }
    }

    sealed class PreviewHotkey : IGlobalHotkey
    {
        // Never raised: nothing here listens for a key press. The accessor is empty on purpose.
        public event Action? Pressed
        {
            add { }
            remove { }
        }

        public MacHotkey? Current { get; private set; }

        public bool TryRegister(MacHotkey hotkey)
        {
            if (!hotkey.IsValid) return false;

            Current = hotkey;
            return true;
        }

        public void Dispose() => Current = null;
    }
}
