namespace AutoTyper.Mac.Tests;

// The registration logic of MacHotkeyService, run against a fake that records every Carbon call and can
// simulate a key press. Nothing here calls into macOS; that the real calls work is for a Mac to show.
public class MacHotkeyServiceTests
{
    const int EventHotKeyExistsErr = -9878;

    static readonly MacHotkey ControlOptionT = new(MacModifiers.Control | MacModifiers.Option, MacKey.T);
    static readonly MacHotkey CommandShiftF9 = new(MacModifiers.Command | MacModifiers.Shift, MacKey.F9);
    static readonly MacHotkey ControlOptionSpace = new(MacModifiers.Control | MacModifiers.Option, MacKey.Space);

    [Fact]
    public void FirstRegistration_InstallsTheHandlerOnce_AndRegistersTheCombination()
    {
        var rig = new Rig();

        var registered = rig.Service.TryRegister(ControlOptionT);

        Assert.True(registered);
        // Key code 0x11 is kVK_ANSI_T; 0x1800 is controlKey (0x1000) | optionKey (0x800).
        Assert.Equal(
            new[] { "install", "register key=17 mods=1800 id=1 -> 2000" },
            rig.Native.Calls);
    }

    [Fact]
    public void ChangingTheHotkey_RegistersTheNewOneBeforeReleasingTheOld_UsingTheOtherId()
    {
        var rig = new Rig();

        Assert.True(rig.Service.TryRegister(ControlOptionT));
        Assert.True(rig.Service.TryRegister(CommandShiftF9));
        Assert.True(rig.Service.TryRegister(ControlOptionSpace));

        Assert.Equal(
            new[]
            {
                "install",
                Register(ControlOptionT, id: 1, handle: 0),
                Register(CommandShiftF9, id: 2, handle: 1),
                Unregister(handle: 0),
                Register(ControlOptionSpace, id: 1, handle: 2),
                Unregister(handle: 1),
            },
            rig.Native.Calls);
    }

    [Fact]
    public void FailedChange_KeepsThePreviousHotkeyRegistered_AndCurrentUnchanged()
    {
        var rig = new Rig();
        Assert.True(rig.Service.TryRegister(ControlOptionT));
        rig.Native.Calls.Clear();

        rig.Native.RegisterStatus = EventHotKeyExistsErr;
        var changed = rig.Service.TryRegister(CommandShiftF9);

        Assert.False(changed);
        Assert.Equal(ControlOptionT, rig.Service.Current);
        // The old one was never released: the only call is the refused registration.
        Assert.Equal(new[] { RefusedRegister(CommandShiftF9, id: 2) }, rig.Native.Calls);

        // The refused attempt did not use up the other id: the next change takes it, then releases the old one.
        rig.Native.Calls.Clear();
        rig.Native.RegisterStatus = 0;
        Assert.True(rig.Service.TryRegister(ControlOptionSpace));
        Assert.Equal(
            new[] { Register(ControlOptionSpace, id: 2, handle: 1), Unregister(handle: 0) },
            rig.Native.Calls);
        Assert.Equal(ControlOptionSpace, rig.Service.Current);
    }

    [Fact]
    public void RegisteringTheCurrentHotkeyAgain_ReturnsTrue_WithoutCallingNative()
    {
        var rig = new Rig();
        Assert.True(rig.Service.TryRegister(ControlOptionT));
        rig.Native.Calls.Clear();

        // A different object with the same value: records compare by value.
        var again = rig.Service.TryRegister(new MacHotkey(MacModifiers.Control | MacModifiers.Option, MacKey.T));

        Assert.True(again);
        Assert.Empty(rig.Native.Calls);
        Assert.Equal(ControlOptionT, rig.Service.Current);
    }

    [Theory]
    [InlineData(MacModifiers.None, MacKey.A)]                  // a plain letter with no modifier
    [InlineData(MacModifiers.Command, (MacKey)0x7F)]           // not a key the app knows
    [InlineData((MacModifiers)0x40, MacKey.T)]                 // not a modifier the app knows
    public void InvalidHotkey_IsRejected_WithoutCallingNative(MacModifiers modifiers, MacKey key)
    {
        var rig = new Rig();
        var invalid = new MacHotkey(modifiers, key);
        Assert.False(invalid.IsValid);

        Assert.False(rig.Service.TryRegister(invalid));
        Assert.Empty(rig.Native.Calls);
        Assert.Null(rig.Service.Current);

        // Also when something valid is registered: it stays, and still nothing more is called.
        Assert.True(rig.Service.TryRegister(ControlOptionT));
        rig.Native.Calls.Clear();

        Assert.False(rig.Service.TryRegister(invalid));
        Assert.Empty(rig.Native.Calls);
        Assert.Equal(ControlOptionT, rig.Service.Current);
    }

    [Fact]
    public void HandlerInstallFailure_MakesTryRegisterFalse()
    {
        var rig = new Rig();
        rig.Native.InstallSucceeds = false;

        Assert.False(rig.Service.TryRegister(ControlOptionT));

        Assert.Null(rig.Service.Current);
        Assert.Equal(new[] { "install" }, rig.Native.Calls);   // no hotkey was registered without a handler

        // Nothing was installed, so there is nothing to remove.
        rig.Service.Dispose();
        Assert.Equal(new[] { "install" }, rig.Native.Calls);
    }

    [Fact]
    public void HandlerInstallFailure_IsRetriedOnTheNextTryRegister()
    {
        var rig = new Rig();
        rig.Native.InstallSucceeds = false;
        Assert.False(rig.Service.TryRegister(ControlOptionT));

        rig.Native.InstallSucceeds = true;
        Assert.True(rig.Service.TryRegister(ControlOptionT));

        Assert.Equal(
            new[] { "install", "install", Register(ControlOptionT, id: 1, handle: 0) },
            rig.Native.Calls);
    }

    [Fact]
    public void FirstRegistrationRefused_LeavesCurrentNull_AndInstallsNoSecondHandler()
    {
        var rig = new Rig();
        rig.Native.RegisterStatus = EventHotKeyExistsErr;

        Assert.False(rig.Service.TryRegister(ControlOptionT));
        Assert.Null(rig.Service.Current);

        rig.Native.RegisterStatus = 0;
        Assert.True(rig.Service.TryRegister(CommandShiftF9));

        Assert.Equal(CommandShiftF9, rig.Service.Current);
        Assert.Equal(
            new[]
            {
                "install",
                RefusedRegister(ControlOptionT, id: 1),
                Register(CommandShiftF9, id: 1, handle: 0),   // the refused attempt did not use up id 1
            },
            rig.Native.Calls);
    }

    [Fact]
    public void AnyNonZeroStatus_MeansFalse()
    {
        foreach (var status in new[] { -50, 1, EventHotKeyExistsErr, int.MinValue })
        {
            var rig = new Rig();
            rig.Native.RegisterStatus = status;

            Assert.False(rig.Service.TryRegister(ControlOptionT), $"status {status}");
            Assert.Null(rig.Service.Current);
        }
    }

    [Fact]
    public void Press_RaisesPressedOnlyThroughPostToUiThread()
    {
        var rig = new Rig();
        var raised = 0;
        rig.Service.Pressed += () => raised++;
        Assert.True(rig.Service.TryRegister(ControlOptionT));

        var status = rig.Native.Press();

        Assert.Equal(0, status);               // noErr, returned to Carbon at once
        Assert.Equal(0, raised);               // nothing happened on the thread Carbon called us on
        var posted = Assert.Single(rig.Posted);

        posted();

        Assert.Equal(1, raised);               // the UI thread ran it
    }

    [Fact]
    public void EachPress_PostsItsOwnRaise()
    {
        var rig = new Rig();
        var raised = 0;
        rig.Service.Pressed += () => raised++;
        Assert.True(rig.Service.TryRegister(ControlOptionT));

        rig.Native.Press();
        rig.Native.Press();
        Assert.Equal(2, rig.Posted.Count);
        foreach (var posted in rig.Posted) posted();

        Assert.Equal(2, raised);
    }

    [Fact]
    public void APressStillWaitingWhenTheServiceIsDisposed_RaisesNothing()
    {
        var rig = new Rig();
        var raised = 0;
        rig.Service.Pressed += () => raised++;
        Assert.True(rig.Service.TryRegister(ControlOptionT));
        rig.Native.Press();

        rig.Service.Dispose();
        foreach (var posted in rig.Posted) posted();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Dispose_UnregistersTheHotkey_AndRemovesTheHandler_Once()
    {
        var rig = new Rig();
        Assert.True(rig.Service.TryRegister(ControlOptionT));
        rig.Native.Calls.Clear();

        rig.Service.Dispose();
        rig.Service.Dispose();

        // The hotkey goes first, then the handler; the second Dispose calls nothing.
        Assert.Equal(new[] { Unregister(handle: 0), "remove 1000" }, rig.Native.Calls);
        Assert.Null(rig.Service.Current);
    }

    [Fact]
    public void Dispose_AfterAHotkeyChange_ReleasesOnlyTheCurrentOne()
    {
        var rig = new Rig();
        Assert.True(rig.Service.TryRegister(ControlOptionT));
        Assert.True(rig.Service.TryRegister(CommandShiftF9));
        rig.Native.Calls.Clear();

        rig.Service.Dispose();

        Assert.Equal(new[] { Unregister(handle: 1), "remove 1000" }, rig.Native.Calls);
    }

    [Fact]
    public void Dispose_AfterAFailedFirstRegistration_RemovesTheHandlerOnly()
    {
        var rig = new Rig();
        rig.Native.RegisterStatus = EventHotKeyExistsErr;
        Assert.False(rig.Service.TryRegister(ControlOptionT));
        rig.Native.Calls.Clear();

        rig.Service.Dispose();

        Assert.Equal(new[] { "remove 1000" }, rig.Native.Calls);
    }

    [Fact]
    public void Dispose_WhenNothingWasRegistered_CallsNoNative()
    {
        var rig = new Rig();

        rig.Service.Dispose();

        Assert.Empty(rig.Native.Calls);
    }

    [Fact]
    public void TryRegister_AfterDispose_ReturnsFalse()
    {
        var rig = new Rig();
        Assert.True(rig.Service.TryRegister(ControlOptionT));
        rig.Service.Dispose();
        rig.Native.Calls.Clear();

        Assert.False(rig.Service.TryRegister(CommandShiftF9));
        Assert.False(rig.Service.TryRegister(ControlOptionT));   // not even the one it had

        Assert.Empty(rig.Native.Calls);
        Assert.Null(rig.Service.Current);
    }

    [Fact]
    public void Current_ReflectsTheRegisteredHotkey()
    {
        var rig = new Rig();
        Assert.Null(rig.Service.Current);

        Assert.True(rig.Service.TryRegister(ControlOptionT));
        Assert.Equal(ControlOptionT, rig.Service.Current);

        Assert.True(rig.Service.TryRegister(CommandShiftF9));
        Assert.Equal(CommandShiftF9, rig.Service.Current);

        rig.Native.RegisterStatus = EventHotKeyExistsErr;
        Assert.False(rig.Service.TryRegister(ControlOptionSpace));
        Assert.Equal(CommandShiftF9, rig.Service.Current);
    }

    // The real service and the real wrapper must be creatable, and disposable while unused, on a machine
    // where any native call would throw (Windows).
    [Fact]
    public void PublicConstructor_AndDisposeWithoutRegistering_TouchNoNativeCode()
    {
        var service = new MacHotkeyService(action => action());

        Assert.Null(service.Current);
        service.Dispose();
        Assert.False(service.TryRegister(ControlOptionT));   // disposed: refused before any native call
    }

    [Fact]
    public void TheRealWrapper_IsCreatedWithoutTouchingNativeCode_AndUnregisteringNothingIsHarmless()
    {
        var native = new CarbonHotkeyNative();

        native.RemoveHandler(IntPtr.Zero);
        native.UnregisterHotkey(IntPtr.Zero);

        Assert.NotNull(native);
    }

    [Fact]
    public void TheRealWrapper_UsesTheKeyboardHotKeyPressedEventAndTheAtypSignature()
    {
        Assert.Equal(0x6B657962u, CarbonHotkeyNative.EventClassKeyboard);        // 'keyb'
        Assert.Equal(5u, CarbonHotkeyNative.EventKindHotKeyPressed);             // kEventHotKeyPressed
        Assert.Equal(0x41545950u, CarbonHotkeyNative.HotKeySignature);           // 'ATYP'
    }

    [Fact]
    public void TheRealHandler_PassesTheStatusOn_AndNeverLetsAnExceptionIntoNativeCode()
    {
        Assert.Equal(0, CarbonHotkeyNative.Dispatch(() => 0));
        Assert.Equal(-1, CarbonHotkeyNative.Dispatch(() => -1));

        // An exception that crossed back into Carbon would take the whole process down.
        Assert.Equal(0, CarbonHotkeyNative.Dispatch(() => throw new InvalidOperationException("boom")));
    }

    // The fake's handles: the handler is 0x1000, and the n-th hotkey registered gets 0x2000 + n.
    static string Register(MacHotkey hotkey, uint id, int handle) =>
        $"register key={hotkey.KeyCode} mods={hotkey.CarbonModifiers:x} id={id} -> {0x2000 + handle:x}";

    static string RefusedRegister(MacHotkey hotkey, uint id) =>
        $"register key={hotkey.KeyCode} mods={hotkey.CarbonModifiers:x} id={id} -> refused";

    static string Unregister(int handle) => $"unregister {0x2000 + handle:x}";

    sealed class Rig
    {
        public Rig()
        {
            Service = new MacHotkeyService(Posted.Add, Native);
        }

        public FakeCarbon Native { get; } = new();

        // What the service handed to postToUiThread and nobody has run yet.
        public List<Action> Posted { get; } = new();

        public MacHotkeyService Service { get; }
    }

    // Records every call as text, in order, and plays Carbon's part when asked to press the key.
    sealed class FakeCarbon : ICarbonHotkeyNative
    {
        Func<int>? handler;
        int nextHotkeyHandle = 0x2000;

        public List<string> Calls { get; } = new();

        public bool InstallSucceeds { get; set; } = true;

        // What RegisterHotkey answers: 0 is noErr.
        public int RegisterStatus { get; set; }

        public bool TryInstallHandler(Func<int> onPressed, out IntPtr handlerHandle)
        {
            Calls.Add("install");
            if (!InstallSucceeds)
            {
                handlerHandle = IntPtr.Zero;
                return false;
            }

            handler = onPressed;
            handlerHandle = (IntPtr)0x1000;
            return true;
        }

        public void RemoveHandler(IntPtr handlerHandle)
        {
            Calls.Add($"remove {handlerHandle.ToInt64():x}");
            handler = null;
        }

        public int RegisterHotkey(uint keyCode, uint carbonModifiers, uint id, out IntPtr hotkeyHandle)
        {
            var call = $"register key={keyCode} mods={carbonModifiers:x} id={id} -> ";
            if (RegisterStatus != 0)
            {
                Calls.Add(call + "refused");
                hotkeyHandle = IntPtr.Zero;
                return RegisterStatus;
            }

            hotkeyHandle = (IntPtr)nextHotkeyHandle++;
            Calls.Add(call + hotkeyHandle.ToInt64().ToString("x"));
            return 0;
        }

        public void UnregisterHotkey(IntPtr hotkeyHandle) => Calls.Add($"unregister {hotkeyHandle.ToInt64():x}");

        // Carbon calling the installed handler on its own thread; returns what the handler answers.
        public int Press()
        {
            Assert.NotNull(handler);
            return handler!();
        }
    }
}
