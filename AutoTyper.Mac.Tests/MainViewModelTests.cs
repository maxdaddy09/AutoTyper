using System.Text.Json;

namespace AutoTyper.Mac.Tests;

public sealed class MainViewModelTests : IDisposable
{
    const string PermissionMessage =
        "AutoTyper needs permission to type. Open Accessibility settings, switch AutoTyper on, then try again.";

    const string PreviewMessage = "Preview: typing and the hotkey only work on macOS";

    // Only a safety net so a broken test fails instead of hanging; nothing waits this long normally.
    static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    sealed class RecordingSender : IKeySender
    {
        public List<string> Calls { get; } = new();
        public Action<string>? Observer { get; set; }
        public int? FailOnCall { get; set; }
        public Exception? ThrowWith { get; set; }

        bool Record(string call)
        {
            if (ThrowWith is not null) throw ThrowWith;
            Calls.Add(call);
            Observer?.Invoke(call);
            return Calls.Count != FailOnCall;
        }

        public bool SendText(string text) => Record($"T:{text}");
        public bool SendEnter() => Record("Enter");
        public bool SendTab() => Record("Tab");
    }

    sealed class FakeHotkey : IGlobalHotkey
    {
        public event Action? Pressed;
        public MacHotkey? Current { get; private set; }
        public List<MacHotkey> Attempts { get; } = new();
        public bool Refuse { get; set; }
        public bool IsDisposed { get; private set; }

        public bool TryRegister(MacHotkey hotkey)
        {
            Attempts.Add(hotkey);
            if (Refuse) return false;
            Current = hotkey;
            return true;
        }

        public void RaisePressed() => Pressed?.Invoke();

        public void Dispose()
        {
            IsDisposed = true;
            Current = null;
        }
    }

    sealed class FakePlatform : IPlatformServices
    {
        readonly IKeySender sender;
        readonly IGlobalHotkey hotkey;

        public FakePlatform(IKeySender sender, IGlobalHotkey hotkey)
        {
            this.sender = sender;
            this.hotkey = hotkey;
        }

        public bool IsPreview { get; set; }
        public bool HasPermission { get; set; } = true;

        // AreModifiersDown answers true this many times, then false.
        public int PollsUntilReleased { get; set; }

        public bool ModifiersStuck { get; set; }
        public List<MacModifiers> ModifierQueries { get; } = new();
        public int SettingsOpened { get; private set; }

        public IKeySender CreateKeySender() => sender;
        public IGlobalHotkey CreateHotkey() => hotkey;
        public bool HasInputPermission() => HasPermission;
        public void OpenPermissionSettings() => SettingsOpened++;

        public bool AreModifiersDown(MacModifiers modifiers)
        {
            ModifierQueries.Add(modifiers);
            if (ModifiersStuck) return true;
            if (PollsUntilReleased <= 0) return false;
            PollsUntilReleased--;
            return true;
        }
    }

    // The injected delay and clock. A delay moves the clock forward by the time it was asked to wait
    // and returns at once, so a job that would take seconds runs instantly.
    sealed class FakeTime
    {
        readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public double NowMs { get; private set; }
        public List<int> Delays { get; } = new();
        public Action<int>? Observer { get; set; }

        // Holds the job at its first delay until Release() or cancellation, so a test can act mid-run.
        public bool Block { get; set; }

        public Task Entered => entered.Task;

        public Task Delay(int milliseconds, CancellationToken ct)
        {
            Delays.Add(milliseconds);
            Observer?.Invoke(milliseconds);
            NowMs += milliseconds;
            if (Block)
            {
                entered.TrySetResult();
                return released.Task.WaitAsync(ct);
            }

            return ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;
        }

        public double Clock() => NowMs;

        public void Release() => released.TrySetResult();
    }

    readonly string folder = Path.Combine(Path.GetTempPath(), "AutoTyperMacTests-" + Guid.NewGuid().ToString("N"));
    readonly RecordingSender sender = new();
    readonly FakeHotkey hotkey = new();
    readonly FakePlatform platform;
    readonly FakeTime time = new();

    // No delay after each letter unless a test asks for one, so Delays shows only what that test is about.
    readonly Settings settings = new() { DelayMs = 0 };

    // Every send and every delay, in the order they happened.
    readonly List<string> events = new();

    readonly List<MainViewModel> created = new();
    bool windowActive;

    public MainViewModelTests()
    {
        platform = new FakePlatform(sender, hotkey);
        sender.Observer = call => events.Add(call);
        time.Observer = ms => events.Add($"delay:{ms}");
    }

    public void Dispose()
    {
        foreach (var viewModel in created) viewModel.Dispose();
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }

    string SettingsPath => Path.Combine(folder, "settings.json");

    MainViewModel Create(string? settingsPath = null)
    {
        var viewModel = new MainViewModel(
            platform, settings, settingsPath ?? SettingsPath, () => windowActive, time.Delay, time.Clock);
        created.Add(viewModel);
        return viewModel;
    }

    MainViewModel StartedWith(string text)
    {
        var viewModel = Create();
        viewModel.Start();
        viewModel.Text = text;
        return viewModel;
    }

    static List<string> RecordStatuses(MainViewModel viewModel)
    {
        var seen = new List<string>();
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Status)) seen.Add(viewModel.Status);
        };
        return seen;
    }

    // ---- Start ----

    [Fact]
    public void Start_RegistersDefaultHotkey_AndShowsReady()
    {
        var viewModel = Create();

        viewModel.Start();

        Assert.Equal("Control+Option+T", viewModel.HotkeyText);
        Assert.Equal("Ready", viewModel.Status);
        Assert.False(viewModel.NeedsPermission);
        Assert.Equal(MacHotkey.Default, hotkey.Current);
        Assert.Equal("Control+Option+T", settings.Hotkey);
    }

    [Fact]
    public void Start_StoredWindowsHotkey_FallsBackToDefault()
    {
        settings.Hotkey = "Ctrl+Alt+T";
        var viewModel = Create();

        viewModel.Start();

        Assert.Equal("Control+Option+T", viewModel.HotkeyText);
        Assert.Equal("Control+Option+T", settings.Hotkey);
        Assert.Equal(MacHotkey.Default, hotkey.Current);
    }

    [Fact]
    public void Start_StoredMacHotkey_IsRegistered()
    {
        settings.Hotkey = "Command+Shift+F9";
        var viewModel = Create();

        viewModel.Start();

        var expected = new MacHotkey(MacModifiers.Shift | MacModifiers.Command, MacKey.F9);
        Assert.Equal(expected, hotkey.Current);
        Assert.Equal("Shift+Command+F9", viewModel.HotkeyText);
    }

    [Fact]
    public void Start_HotkeyRefused_ShowsInUse_AndHotkeyTextNone()
    {
        hotkey.Refuse = true;
        var viewModel = Create();

        viewModel.Start();

        Assert.Equal("(none)", viewModel.HotkeyText);
        Assert.Equal("Hotkey Control+Option+T is in use — click the hotkey box and pick another", viewModel.Status);
    }

    [Fact]
    public void Start_WithoutPermission_ShowsPermissionMessage_AndNeedsPermission()
    {
        platform.HasPermission = false;
        var viewModel = Create();

        viewModel.Start();

        Assert.True(viewModel.NeedsPermission);
        Assert.Equal(PermissionMessage, viewModel.Status);
        Assert.Equal("Control+Option+T", viewModel.HotkeyText);
    }

    [Fact]
    public void Start_InPreview_ShowsPreviewMessage()
    {
        platform.IsPreview = true;
        var viewModel = Create();

        viewModel.Start();

        Assert.Equal(PreviewMessage, viewModel.Status);
    }

    [Fact]
    public void Constructor_LoadsSettingsClamped_AndStartDoesNotSaveThem()
    {
        settings.Mode = TypingMode.Words;
        settings.DelayMs = 99999;
        settings.UseWpm = true;
        settings.Wpm = 0;

        var viewModel = Create();
        viewModel.Start();

        Assert.Equal(TypingMode.Words, viewModel.Mode);
        Assert.Equal(Settings.MaxDelayMs, viewModel.DelayMs);
        Assert.True(viewModel.UseWpm);
        Assert.Equal(Settings.MinWpm, viewModel.Wpm);
        Assert.False(File.Exists(SettingsPath));
    }

    // ---- Pressing the hotkey: guards ----

    [Fact]
    public async Task Pressed_EmptyText_SaysNothingToType()
    {
        var viewModel = StartedWith("");

        await viewModel.OnHotkeyAsync();

        Assert.Equal("Nothing to type", viewModel.Status);
        Assert.Empty(sender.Calls);
        Assert.False(viewModel.IsTyping);
    }

    [Fact]
    public async Task Pressed_OwnWindowActive_SaysClickIntoTheTargetWindow()
    {
        var viewModel = StartedWith("ab");
        windowActive = true;

        await viewModel.OnHotkeyAsync();

        Assert.Equal("Click into the target window first", viewModel.Status);
        Assert.Empty(sender.Calls);
        Assert.False(viewModel.IsTyping);
    }

    [Fact]
    public async Task Pressed_WithoutPermission_TypesNothing()
    {
        var viewModel = StartedWith("ab");
        Assert.False(viewModel.NeedsPermission);

        // Switched off in System Settings while the app was open: the press must notice.
        platform.HasPermission = false;
        await viewModel.OnHotkeyAsync();

        Assert.Empty(sender.Calls);
        Assert.Equal(PermissionMessage, viewModel.Status);
        Assert.True(viewModel.NeedsPermission);
        Assert.False(viewModel.IsTyping);
    }

    [Fact]
    public async Task Pressed_AfterPermissionIsGranted_TypesAndClearsNeedsPermission()
    {
        platform.HasPermission = false;
        var viewModel = StartedWith("ab");
        Assert.True(viewModel.NeedsPermission);

        platform.HasPermission = true;
        await viewModel.OnHotkeyAsync();

        Assert.False(viewModel.NeedsPermission);
        Assert.Equal(new[] { "T:a", "T:b" }, sender.Calls);
        Assert.Equal("Done", viewModel.Status);
    }

    [Fact]
    public async Task Pressed_GuardsRunInOrder_EmptyTextThenPermissionThenOwnWindow()
    {
        var viewModel = StartedWith("");
        platform.HasPermission = false;
        windowActive = true;

        await viewModel.OnHotkeyAsync();
        Assert.Equal("Nothing to type", viewModel.Status);

        viewModel.Text = "ab";
        await viewModel.OnHotkeyAsync();
        Assert.Equal(PermissionMessage, viewModel.Status);

        platform.HasPermission = true;
        await viewModel.OnHotkeyAsync();
        Assert.Equal("Click into the target window first", viewModel.Status);

        Assert.Empty(sender.Calls);
    }

    // ---- Pressing the hotkey: typing ----

    [Fact]
    public async Task Pressed_Letters_TypesTextThenDone()
    {
        var viewModel = StartedWith("ab");
        var statuses = RecordStatuses(viewModel);

        await viewModel.OnHotkeyAsync();

        Assert.Equal(new[] { "T:a", "T:b" }, sender.Calls);
        Assert.Equal(new[] { "Typing…", "Done" }, statuses);
        Assert.False(viewModel.IsTyping);
    }

    [Fact]
    public async Task Pressed_WordsWithWpm_UsesTheScheduledPace()
    {
        var viewModel = StartedWith("one two three");
        viewModel.Mode = TypingMode.Words;
        viewModel.UseWpm = true;
        viewModel.Wpm = 120;

        await viewModel.OnHotkeyAsync();

        Assert.Equal(new[] { 500, 500 }, time.Delays);
        Assert.Equal(new[] { "T:one ", "T:two ", "T:three" }, sender.Calls);
        Assert.Equal("Done", viewModel.Status);
    }

    [Fact]
    public async Task Pressed_WordsWithoutWpm_UsesTheDelay()
    {
        var viewModel = StartedWith("a b c");
        viewModel.Mode = TypingMode.Words;
        viewModel.DelayMs = 25;

        await viewModel.OnHotkeyAsync();

        Assert.Equal(new[] { 25, 25 }, time.Delays);
        Assert.Equal(new[] { "T:a ", "T:b ", "T:c" }, sender.Calls);
    }

    [Fact]
    public async Task Pressed_LettersIgnoresWpm()
    {
        var viewModel = StartedWith("abc");
        viewModel.UseWpm = true;
        viewModel.Wpm = 120;
        viewModel.DelayMs = 25;

        await viewModel.OnHotkeyAsync();

        Assert.Equal(new[] { 25, 25 }, time.Delays);
        Assert.Equal(new[] { "T:a", "T:b", "T:c" }, sender.Calls);
    }

    [Fact]
    public async Task Pressed_TextAndPaceChangedWhileTyping_DoNotAffectTheRunningJob()
    {
        var viewModel = StartedWith("abc");
        viewModel.DelayMs = 50;
        time.Block = true;

        var job = viewModel.OnHotkeyAsync();
        await time.Entered.WaitAsync(Patience);

        viewModel.Text = "zzz";
        viewModel.Mode = TypingMode.Words;
        viewModel.DelayMs = 1;
        time.Release();
        await job.WaitAsync(Patience);

        Assert.Equal(new[] { "T:a", "T:b", "T:c" }, sender.Calls);
        Assert.Equal(new[] { 50, 50 }, time.Delays);
        Assert.Equal("Done", viewModel.Status);
    }

    // ---- Waiting for the hotkey's modifiers ----

    [Fact]
    public async Task Pressed_WaitsForModifiersToBeReleased()
    {
        var viewModel = StartedWith("a");
        platform.PollsUntilReleased = 3;

        await viewModel.OnHotkeyAsync();

        Assert.Equal(new[] { "delay:10", "delay:10", "delay:10", "T:a" }, events);
        Assert.All(platform.ModifierQueries, queried => Assert.Equal(MacModifiers.Control | MacModifiers.Option, queried));
        Assert.Equal("Done", viewModel.Status);
    }

    [Fact]
    public async Task Pressed_ModifiersNeverReleased_TypesAfterThreeSeconds()
    {
        var viewModel = StartedWith("ab");
        platform.ModifiersStuck = true;

        await viewModel.OnHotkeyAsync();

        Assert.Equal(300, time.Delays.Count(delay => delay == 10));
        Assert.Equal(300, time.Delays.Count);
        Assert.Equal(3000, time.NowMs);
        Assert.Equal(new[] { "T:a", "T:b" }, sender.Calls);
        Assert.Equal("Done", viewModel.Status);
    }

    [Fact]
    public async Task Pressed_HotkeyWithoutModifiers_DoesNotWait()
    {
        settings.Hotkey = "F9";
        platform.ModifiersStuck = true;
        var viewModel = StartedWith("ab");

        await viewModel.OnHotkeyAsync();

        Assert.Empty(time.Delays);
        Assert.Equal(new[] { "T:a", "T:b" }, sender.Calls);
        Assert.Equal("Done", viewModel.Status);
    }

    [Fact]
    public async Task PressedAgainWhileWaitingForModifiers_StopsBeforeTyping()
    {
        var viewModel = StartedWith("ab");
        platform.ModifiersStuck = true;
        time.Block = true;

        var job = viewModel.OnHotkeyAsync();
        await time.Entered.WaitAsync(Patience);
        await viewModel.OnHotkeyAsync().WaitAsync(Patience);
        await job.WaitAsync(Patience);

        Assert.Empty(sender.Calls);
        Assert.Equal("Stopped", viewModel.Status);
        Assert.False(viewModel.IsTyping);
    }

    // ---- Stopping, failing, closing ----

    [Fact]
    public async Task PressedAgainWhileTyping_StopsAndShowsStopped()
    {
        var viewModel = StartedWith("ab");
        viewModel.DelayMs = 50;
        time.Block = true;

        var job = viewModel.OnHotkeyAsync();
        await time.Entered.WaitAsync(Patience);
        Assert.True(viewModel.IsTyping);

        // The stopping press returns at once; the first call finishes the job.
        await viewModel.OnHotkeyAsync().WaitAsync(Patience);
        await job.WaitAsync(Patience);

        Assert.Equal(new[] { "T:a" }, sender.Calls);
        Assert.Equal("Stopped", viewModel.Status);
        Assert.False(viewModel.IsTyping);
    }

    [Fact]
    public async Task HotkeyPressedEvent_StartsTyping()
    {
        var viewModel = StartedWith("ab");
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.Status) && viewModel.Status == "Done") done.TrySetResult();
        };

        hotkey.RaisePressed();
        await done.Task.WaitAsync(Patience);

        Assert.Equal(new[] { "T:a", "T:b" }, sender.Calls);
    }

    [Fact]
    public async Task SendFailure_ShowsTypingFailed()
    {
        var viewModel = StartedWith("ab");
        sender.FailOnCall = 1;

        await viewModel.OnHotkeyAsync();

        Assert.Equal("Typing failed — macOS did not accept the input", viewModel.Status);
        Assert.Equal(new[] { "T:a" }, sender.Calls);
        Assert.False(viewModel.IsTyping);
    }

    [Fact]
    public async Task ExceptionInTheJob_ShowsError()
    {
        var viewModel = StartedWith("ab");
        sender.ThrowWith = new InvalidOperationException("boom");

        await viewModel.OnHotkeyAsync();

        Assert.Equal("Error: boom", viewModel.Status);
        Assert.False(viewModel.IsTyping);
    }

    [Fact]
    public async Task Pressed_AfterAJobEnded_StartsANewOne()
    {
        var viewModel = StartedWith("ab");
        sender.FailOnCall = 1;
        await viewModel.OnHotkeyAsync();
        Assert.Equal("Typing failed — macOS did not accept the input", viewModel.Status);

        sender.FailOnCall = null;
        sender.Calls.Clear();
        await viewModel.OnHotkeyAsync();

        Assert.Equal(new[] { "T:a", "T:b" }, sender.Calls);
        Assert.Equal("Done", viewModel.Status);
    }

    [Fact]
    public async Task Dispose_CancelsARunningJob_AndDisposesTheHotkey()
    {
        var viewModel = StartedWith("ab");
        viewModel.DelayMs = 50;
        time.Block = true;

        var job = viewModel.OnHotkeyAsync();
        await time.Entered.WaitAsync(Patience);

        viewModel.Dispose();
        await job.WaitAsync(Patience);

        // Nothing keeps typing, and a late press does nothing.
        Assert.Equal(new[] { "T:a" }, sender.Calls);
        Assert.True(hotkey.IsDisposed);
        Assert.False(viewModel.IsTyping);

        hotkey.RaisePressed();
        await viewModel.OnHotkeyAsync().WaitAsync(Patience);
        Assert.Equal(new[] { "T:a" }, sender.Calls);
    }

    // ---- Progress ----

    [Fact]
    public async Task RefreshProgress_ShowsSentOfTotal()
    {
        var viewModel = StartedWith("abc");
        viewModel.DelayMs = 50;
        time.Block = true;

        var job = viewModel.OnHotkeyAsync();
        await time.Entered.WaitAsync(Patience);
        viewModel.RefreshProgress();

        Assert.Equal($"Typing… {1:N0}/{3:N0}", viewModel.Status);

        await viewModel.OnHotkeyAsync().WaitAsync(Patience);
        await job.WaitAsync(Patience);
    }

    [Fact]
    public async Task RefreshProgress_WithoutARunningJob_ChangesNothing()
    {
        var viewModel = StartedWith("ab");
        viewModel.RefreshProgress();
        Assert.Equal("Ready", viewModel.Status);

        await viewModel.OnHotkeyAsync();
        viewModel.RefreshProgress();

        Assert.Equal("Done", viewModel.Status);
    }

    // ---- Changing the hotkey ----

    [Fact]
    public void TryChangeHotkey_Invalid_KeepsOldAndExplains()
    {
        var viewModel = Create();
        viewModel.Start();
        var invalid = new MacHotkey(MacModifiers.None, MacKey.T);

        var changed = viewModel.TryChangeHotkey(invalid);

        Assert.False(changed);
        Assert.Equal("That hotkey can't be used — hold Control, Option, Shift or Command, or use an F-key", viewModel.Status);
        Assert.Equal("Control+Option+T", viewModel.HotkeyText);
        Assert.Equal(MacHotkey.Default, hotkey.Current);
        Assert.Equal("Control+Option+T", settings.Hotkey);
        Assert.DoesNotContain(invalid, hotkey.Attempts);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void TryChangeHotkey_Refused_KeepsOldAndExplains()
    {
        var viewModel = Create();
        viewModel.Start();
        hotkey.Refuse = true;

        var changed = viewModel.TryChangeHotkey(new MacHotkey(MacModifiers.Command | MacModifiers.Shift, MacKey.K));

        Assert.False(changed);
        Assert.Equal("Hotkey Shift+Command+K is in use — pick another", viewModel.Status);
        Assert.Equal("Control+Option+T", viewModel.HotkeyText);
        Assert.Equal(MacHotkey.Default, hotkey.Current);
        Assert.Equal("Control+Option+T", settings.Hotkey);
        Assert.False(File.Exists(SettingsPath));
    }

    [Fact]
    public void TryChangeHotkey_Success_SavesAndShowsReady()
    {
        var viewModel = Create();
        viewModel.Start();
        var candidate = new MacHotkey(MacModifiers.Command | MacModifiers.Shift, MacKey.K);

        var changed = viewModel.TryChangeHotkey(candidate);

        Assert.True(changed);
        Assert.Equal("Ready", viewModel.Status);
        Assert.Equal("Shift+Command+K", viewModel.HotkeyText);
        Assert.Equal(candidate, hotkey.Current);
        Assert.Equal("Shift+Command+K", Settings.Load(SettingsPath).Hotkey);
    }

    [Fact]
    public void TryChangeHotkey_WithoutPermission_KeepsThePermissionMessage()
    {
        platform.HasPermission = false;
        var viewModel = Create();
        viewModel.Start();

        var changed = viewModel.TryChangeHotkey(new MacHotkey(MacModifiers.Command, MacKey.K));

        Assert.True(changed);
        Assert.Equal(PermissionMessage, viewModel.Status);
    }

    // ---- Pace controls and settings ----

    static void AssertPaceControls(MainViewModel viewModel, bool available, bool enabled, bool delayEnabled)
    {
        Assert.Equal(available, viewModel.WpmAvailable);
        Assert.Equal(enabled, viewModel.WpmEnabled);
        Assert.Equal(delayEnabled, viewModel.DelayEnabled);
    }

    static void AssertPaceControlsRaised(List<string?> raised)
    {
        Assert.Contains(nameof(MainViewModel.WpmAvailable), raised);
        Assert.Contains(nameof(MainViewModel.WpmEnabled), raised);
        Assert.Contains(nameof(MainViewModel.DelayEnabled), raised);
        raised.Clear();
    }

    [Fact]
    public void PaceControls_FollowModeAndCheckbox()
    {
        var viewModel = Create();
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        AssertPaceControls(viewModel, available: false, enabled: false, delayEnabled: true);

        viewModel.Mode = TypingMode.Words;
        AssertPaceControls(viewModel, available: true, enabled: false, delayEnabled: true);
        AssertPaceControlsRaised(raised);

        viewModel.UseWpm = true;
        AssertPaceControls(viewModel, available: true, enabled: true, delayEnabled: false);
        AssertPaceControlsRaised(raised);

        viewModel.Mode = TypingMode.Letters;
        AssertPaceControls(viewModel, available: false, enabled: false, delayEnabled: true);
        AssertPaceControlsRaised(raised);
    }

    [Fact]
    public void Settings_AreSavedWhenChanged_AndNeverContainTheText()
    {
        var viewModel = Create();
        viewModel.Start();

        viewModel.Text = "my secret password";
        Assert.False(File.Exists(SettingsPath));

        viewModel.Mode = TypingMode.Words;
        Assert.Equal(TypingMode.Words, Settings.Load(SettingsPath).Mode);

        viewModel.DelayMs = 123;
        Assert.Equal(123, Settings.Load(SettingsPath).DelayMs);

        viewModel.UseWpm = true;
        Assert.True(Settings.Load(SettingsPath).UseWpm);

        viewModel.Wpm = 200;
        Assert.Equal(200, Settings.Load(SettingsPath).Wpm);

        Assert.True(viewModel.TryChangeHotkey(new MacHotkey(MacModifiers.Command | MacModifiers.Shift, MacKey.F9)));
        var reloaded = Settings.Load(SettingsPath);
        Assert.Equal("Shift+Command+F9", reloaded.Hotkey);
        Assert.Equal(TypingMode.Words, reloaded.Mode);
        Assert.Equal(123, reloaded.DelayMs);
        Assert.True(reloaded.UseWpm);
        Assert.Equal(200, reloaded.Wpm);

        var json = File.ReadAllText(SettingsPath);
        Assert.DoesNotContain("secret", json);
        using var document = JsonDocument.Parse(json);
        var names = document.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();
        Assert.Equal(new[] { "DelayMs", "Hotkey", "Mode", "UseWpm", "Wpm" }, names);
    }

    [Fact]
    public void Settings_AreClamped()
    {
        var viewModel = Create();

        viewModel.DelayMs = 99999;
        Assert.Equal(60000, viewModel.DelayMs);
        viewModel.DelayMs = -5;
        Assert.Equal(0, viewModel.DelayMs);

        viewModel.Wpm = 0;
        Assert.Equal(1, viewModel.Wpm);
        viewModel.Wpm = 5000;
        Assert.Equal(1000, viewModel.Wpm);

        var saved = Settings.Load(SettingsPath);
        Assert.Equal(0, saved.DelayMs);
        Assert.Equal(1000, saved.Wpm);
    }

    [Fact]
    public void Settings_ClampedValueEqualToTheOldOne_StillTellsABoundControl()
    {
        var viewModel = Create();
        viewModel.DelayMs = 60000;
        var raised = new List<string?>();
        viewModel.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        viewModel.DelayMs = 99999;

        Assert.Equal(60000, viewModel.DelayMs);
        Assert.Contains(nameof(MainViewModel.DelayMs), raised);
    }

    [Fact]
    public void SaveFailure_ShowsStatus_AndDoesNotThrow()
    {
        // A file where a folder is needed makes saving fail on every operating system.
        Directory.CreateDirectory(folder);
        var blocker = Path.Combine(folder, "blocker");
        File.WriteAllText(blocker, "not a folder");
        var viewModel = Create(Path.Combine(blocker, "settings.json"));
        viewModel.Start();

        viewModel.DelayMs = 5;
        Assert.StartsWith("Could not save settings: ", viewModel.Status);

        // The hotkey did change; the save error is shown rather than hidden behind "Ready".
        Assert.True(viewModel.TryChangeHotkey(new MacHotkey(MacModifiers.Command, MacKey.K)));
        Assert.StartsWith("Could not save settings: ", viewModel.Status);
    }

    // ---- Permission button ----

    [Fact]
    public void OpenPermissionSettings_AsksThePlatform()
    {
        var viewModel = Create();

        viewModel.OpenPermissionSettings();

        Assert.Equal(1, platform.SettingsOpened);
    }
}
