using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace AutoTyper.Mac;

// Everything the window shows and does, without any Avalonia types. It mirrors the job-start logic of
// the Windows MainForm (guards, snapshot, modifier wait, progress, status texts, settings).
//
// Threads: PropertyChanged is raised on whichever thread sets a property, so the window marshals to
// the UI thread. Properties are set by the callers and by the code around the awaited job (which
// resumes on the caller's context), never from inside the thread-pool job, which only writes the two
// progress counters.
public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    const int ModifierPollMs = 10;
    const int ModifierReleaseTimeoutMs = 3000;

    const string NoHotkey = "(none)";
    const string ReadyText = "Ready";
    const string PreviewText = "Preview: typing and the hotkey only work on macOS";
    const string NothingToTypeText = "Nothing to type";
    const string ClickIntoTargetText = "Click into the target window first";

    const string PermissionText =
        "AutoTyper needs permission to type. Open Accessibility settings, switch AutoTyper on, then try again.";

    const string TypingText = "Typing…";
    const string DoneText = "Done";
    const string StoppedText = "Stopped";
    const string SendFailedText = "Typing failed — macOS did not accept the input";
    const string InvalidHotkeyText = "That hotkey can't be used — hold Control, Option, Shift or Command, or use an F-key";

    readonly IPlatformServices platform;
    readonly Settings settings;
    readonly string settingsPath;
    readonly Func<bool> isWindowActive;
    readonly Func<int, CancellationToken, Task> delay;
    readonly Func<double> clock;
    readonly IKeySender keySender;
    readonly IGlobalHotkey hotkey;

    string text = "";
    TypingMode mode;
    int delayMs;
    bool useWpm;
    int wpm;
    string hotkeyText = NoHotkey;
    string status = "";
    bool isTyping;
    bool needsPermission;

    // The running job's cancellation source; null when idle. Claimed and released with Interlocked.
    CancellationTokenSource? job;

    // Written by the typing thread, read by RefreshProgress on the UI thread.
    volatile int unitsSent;
    volatile int unitsTotal;

    bool started;
    int disposed;

    public MainViewModel(
        IPlatformServices platform,
        Settings settings,
        string settingsPath,
        Func<bool> isWindowActive,
        Func<int, CancellationToken, Task>? delay = null,
        Func<double>? clock = null)
    {
        this.platform = platform;
        this.settings = settings;
        this.settingsPath = settingsPath;
        this.isWindowActive = isWindowActive;
        this.delay = delay ?? Task.Delay;
        this.clock = clock ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);

        // Set on the fields, not the properties, so loading the settings does not save them again.
        mode = settings.Mode;
        delayMs = Math.Clamp(settings.DelayMs, 0, Settings.MaxDelayMs);
        useWpm = settings.UseWpm;
        wpm = Math.Clamp(settings.Wpm, Settings.MinWpm, Settings.MaxWpm);

        keySender = platform.CreateKeySender();
        hotkey = platform.CreateHotkey();
        hotkey.Pressed += OnHotkeyPressed;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // The text to type. Deliberately not part of Settings: it may be a password.
    public string Text
    {
        get => text;
        set => Set(ref text, value ?? "");
    }

    public TypingMode Mode
    {
        get => mode;
        set
        {
            if (!Set(ref mode, value)) return;
            RaisePaceControlsChanged();
            SaveSettings();
        }
    }

    public int DelayMs
    {
        get => delayMs;
        set
        {
            var clamped = Math.Clamp(value, 0, Settings.MaxDelayMs);
            if (Set(ref delayMs, clamped)) SaveSettings();
            else if (clamped != value) Raise(nameof(DelayMs)); // so a bound control snaps back
        }
    }

    public bool UseWpm
    {
        get => useWpm;
        set
        {
            if (!Set(ref useWpm, value)) return;
            RaisePaceControlsChanged();
            SaveSettings();
        }
    }

    public int Wpm
    {
        get => wpm;
        set
        {
            var clamped = Math.Clamp(value, Settings.MinWpm, Settings.MaxWpm);
            if (Set(ref wpm, clamped)) SaveSettings();
            else if (clamped != value) Raise(nameof(Wpm));
        }
    }

    // Words per minute only applies in Words mode, and when it is on it replaces the delay box.
    public bool WpmAvailable => Mode == TypingMode.Words;

    public bool WpmEnabled => Mode == TypingMode.Words && UseWpm;

    public bool DelayEnabled => !WpmEnabled;

    public string HotkeyText
    {
        get => hotkeyText;
        private set => Set(ref hotkeyText, value);
    }

    public string Status
    {
        get => status;
        private set => Set(ref status, value);
    }

    public bool IsTyping
    {
        get => isTyping;
        private set => Set(ref isTyping, value);
    }

    // True while the system does not let the app send keystrokes; the window shows the button for it.
    public bool NeedsPermission
    {
        get => needsPermission;
        private set => Set(ref needsPermission, value);
    }

    // Registers the stored hotkey (or the default) and shows the first status. Call once, after the
    // window has subscribed to PropertyChanged.
    public void Start()
    {
        if (started || Volatile.Read(ref disposed) != 0) return;
        started = true;

        NeedsPermission = !platform.HasInputPermission();

        var binding = MacHotkey.TryParse(settings.Hotkey, out var parsed) ? parsed : MacHotkey.Default;
        settings.Hotkey = binding.ToString();

        if (hotkey.TryRegister(binding))
        {
            HotkeyText = binding.ToString();
            Status = IdleStatus();
        }
        else
        {
            HotkeyText = NoHotkey;
            Status = $"Hotkey {binding} is in use — click the hotkey box and pick another";
        }
    }

    // What the status line says when nothing is happening.
    string IdleStatus() =>
        platform.IsPreview ? PreviewText
        : NeedsPermission ? PermissionText
        : ReadyText;

    void OnHotkeyPressed() => _ = OnHotkeyAsync();

    // Starts a job, or stops the running one. The returned task completes when the job it started has
    // finished; a press that only stops a job returns at once. Never throws.
    public async Task OnHotkeyAsync()
    {
        if (Volatile.Read(ref disposed) != 0) return;

        string jobText;
        TypingMode jobMode;
        TypingPace pace;
        MacModifiers modifiers;
        try
        {
            if (CancelRunningJob()) return;

            if (Text.Length == 0)
            {
                Status = NothingToTypeText;
                return;
            }

            // Asked again on every press: the permission can be switched off while the app is open.
            NeedsPermission = !platform.HasInputPermission();
            if (NeedsPermission)
            {
                Status = PermissionText;
                return;
            }

            if (isWindowActive())
            {
                Status = ClickIntoTargetText;
                return;
            }

            // Taken now, so editing the box or changing settings mid-run does not affect this job.
            jobText = Text;
            jobMode = Mode;
            pace = WpmEnabled ? TypingPace.WordsPerMinute(Wpm) : TypingPace.AfterEach(DelayMs);
            modifiers = hotkey.Current?.Modifiers ?? MacModifiers.None;
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
            return;
        }

        var cts = new CancellationTokenSource();
        if (Interlocked.CompareExchange(ref job, cts, null) is { } running)
        {
            // Another press started a job after the check above; this press is the one that stops it.
            Cancel(running);
            cts.Dispose();
            return;
        }

        string finalStatus;
        try
        {
            var engine = new TypingEngine(keySender, delay, clock);
            engine.Progress += (sent, total) =>
            {
                unitsSent = sent;
                unitsTotal = total;
            };

            unitsSent = 0;
            unitsTotal = 0;
            IsTyping = true;
            Status = TypingText;

            // On the thread pool, so a long job with no delay cannot hold up the UI thread.
            var result = await Task.Run(async () =>
            {
                await WaitForModifiersReleased(modifiers, cts.Token);
                return await engine.RunAsync(jobText, jobMode, pace, cts.Token);
            });

            finalStatus = result switch
            {
                TypingResult.Completed => DoneText,
                TypingResult.Cancelled => StoppedText,
                _ => SendFailedText,
            };
        }
        catch (OperationCanceledException)
        {
            finalStatus = StoppedText;
        }
        catch (Exception ex)
        {
            finalStatus = $"Error: {ex.Message}";
        }
        finally
        {
            // Before the final status, so a late RefreshProgress cannot overwrite it.
            Interlocked.CompareExchange(ref job, null, cts);
            IsTyping = false;
            cts.Dispose();
        }

        Status = finalStatus;
    }

    // The hotkey's own modifiers are usually still held when it fires; typing while Control or Option
    // is down would turn the first characters into shortcuts. Gives up after a few seconds and types
    // anyway, so a stuck key cannot hang the job.
    async Task WaitForModifiersReleased(MacModifiers modifiers, CancellationToken ct)
    {
        if (modifiers == MacModifiers.None) return;

        var start = clock();
        while (clock() - start < ModifierReleaseTimeoutMs && platform.AreModifiersDown(modifiers))
            await delay(ModifierPollMs, ct);
    }

    bool CancelRunningJob()
    {
        if (Volatile.Read(ref job) is not { } running) return false;
        Cancel(running);
        return true;
    }

    // The job may finish, and dispose its source, between reading the field and cancelling.
    static void Cancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    // Called by the window's timer while a job runs.
    public void RefreshProgress()
    {
        var total = unitsTotal;
        if (IsTyping && total > 0)
            Status = $"Typing… {unitsSent:N0}/{total:N0}";
    }

    public bool TryChangeHotkey(MacHotkey candidate)
    {
        if (!candidate.IsValid)
        {
            Status = InvalidHotkeyText;
            return false;
        }

        if (!hotkey.TryRegister(candidate))
        {
            Status = $"Hotkey {candidate} is in use — pick another";
            return false;
        }

        HotkeyText = candidate.ToString();
        settings.Hotkey = candidate.ToString();

        // Ready first and the save after it, so a failed save is not hidden behind "Ready".
        NeedsPermission = !platform.HasInputPermission();
        Status = IdleStatus();
        SaveSettings();
        return true;
    }

    public void OpenPermissionSettings() => platform.OpenPermissionSettings();

    // Stops a running job and releases the hotkey, so nothing keeps typing after the window closes.
    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;

        hotkey.Pressed -= OnHotkeyPressed;
        CancelRunningJob();
        hotkey.Dispose();
    }

    void SaveSettings()
    {
        settings.Mode = mode;
        settings.DelayMs = delayMs;
        settings.UseWpm = useWpm;
        settings.Wpm = wpm;
        if (!settings.TrySave(settingsPath, out var error))
            Status = $"Could not save settings: {error}";
    }

    void RaisePaceControlsChanged()
    {
        Raise(nameof(WpmAvailable));
        Raise(nameof(WpmEnabled));
        Raise(nameof(DelayEnabled));
    }

    bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name);
        return true;
    }

    void Raise(string? name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
