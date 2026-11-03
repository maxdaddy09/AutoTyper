namespace AutoTyper;

public sealed class MainForm : Form
{
    const int VkShift = 0x10;
    const int VkControl = 0x11;
    const int VkAlt = 0x12;
    const int VkLeftWin = 0x5B;
    const int VkRightWin = 0x5C;
    const int ModifierReleaseTimeoutMs = 3000;

    readonly Settings settings = Settings.Load(AppPaths.SettingsPath);
    readonly HotkeyRegistrar registrar = new();
    readonly IKeySender keySender = new SendInputKeySender();
    readonly System.Windows.Forms.Timer progressTimer = new() { Interval = 100 };

    readonly TextBox textBox = new()
    {
        Name = "textBox",
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        Dock = DockStyle.Fill,
        // The default of 32,767 characters silently cuts off a long paste.
        MaxLength = int.MaxValue,
    };

    readonly TextBox hotkeyBox = new() { Name = "hotkeyBox", ReadOnly = true, Width = 130 };
    readonly RadioButton lettersRadio = new() { Name = "lettersRadio", Text = "Letters", AutoSize = true };
    readonly RadioButton wordsRadio = new() { Name = "wordsRadio", Text = "Words", AutoSize = true };

    readonly NumericUpDown delayInput = new()
    {
        Name = "delayInput",
        Minimum = 0,
        Maximum = Settings.MaxDelayMs,
        Increment = 10,
        Width = 80,
    };

    readonly CheckBox wpmCheck = new()
    {
        Name = "wpmCheck",
        Text = "Words per minute",
        AutoSize = true,
        Margin = new Padding(8, 4, 4, 0),
    };

    readonly NumericUpDown wpmInput = new()
    {
        Name = "wpmInput",
        Minimum = Settings.MinWpm,
        Maximum = Settings.MaxWpm,
        Increment = 5,
        Width = 70,
    };

    readonly Label statusLabel = new() { Name = "statusLabel", AutoSize = true };

    CancellationTokenSource? job;

    // Written by the typing thread, read by progressTimer on the UI thread.
    volatile int unitsSent;
    volatile int unitsTotal;

    public MainForm()
    {
        BuildLayout();

        lettersRadio.Checked = settings.Mode == TypingMode.Letters;
        wordsRadio.Checked = settings.Mode == TypingMode.Words;
        delayInput.Value = Math.Clamp(settings.DelayMs, 0, Settings.MaxDelayMs);
        wpmCheck.Checked = settings.UseWpm;
        wpmInput.Value = Math.Clamp(settings.Wpm, Settings.MinWpm, Settings.MaxWpm);
        UpdatePaceControls();
        RegisterInitialHotkey();

        // Hooked up after the controls are populated so loading settings does not re-save them.
        lettersRadio.CheckedChanged += (_, _) =>
        {
            UpdatePaceControls();
            SaveSettings();
        };
        wpmCheck.CheckedChanged += (_, _) =>
        {
            UpdatePaceControls();
            SaveSettings();
        };
        delayInput.ValueChanged += (_, _) => SaveSettings();
        wpmInput.ValueChanged += (_, _) => SaveSettings();
        hotkeyBox.KeyDown += OnHotkeyBoxKeyDown;
        registrar.Pressed += OnHotkeyPressed;
        progressTimer.Tick += (_, _) => ShowProgress();
        FormClosing += OnFormClosing;
    }

    TypingMode CurrentMode => lettersRadio.Checked ? TypingMode.Letters : TypingMode.Words;

    bool UsingWpm => CurrentMode == TypingMode.Words && wpmCheck.Checked;

    // Words per minute only applies in Words mode, and when it is on it replaces the delay box.
    void UpdatePaceControls()
    {
        wpmCheck.Enabled = CurrentMode == TypingMode.Words;
        wpmInput.Enabled = UsingWpm;
        delayInput.Enabled = !UsingWpm;
    }

    void BuildLayout()
    {
        Text = "AutoTyper";
        ClientSize = new Size(640, 460);
        MinimumSize = new Size(520, 400);
        StartPosition = FormStartPosition.CenterScreen;

        var note = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = SystemColors.GrayText,
            Text = "Types into the focused window. Windows running as administrator ignore AutoTyper "
                 + "unless AutoTyper is also run as administrator.",
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(12),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.Controls.Add(new Label { Text = "Text to type", AutoSize = true }, 0, 0);
        root.Controls.Add(textBox, 0, 1);
        root.Controls.Add(BuildSettingsRow(), 0, 2);
        root.Controls.Add(statusLabel, 0, 3);
        root.Controls.Add(note, 0, 4);
        Controls.Add(root);
    }

    FlowLayoutPanel BuildSettingsRow()
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        row.Controls.Add(FieldLabel("Hotkey"));
        row.Controls.Add(hotkeyBox);
        row.Controls.Add(FieldLabel("Mode"));
        row.Controls.Add(lettersRadio);
        row.Controls.Add(wordsRadio);
        row.SetFlowBreak(wordsRadio, true);

        // Each label stays on the same line as its box.
        var delayGroup = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        delayGroup.Controls.Add(FieldLabel("Delay (ms)"));
        delayGroup.Controls.Add(delayInput);
        row.Controls.Add(delayGroup);

        var wpmGroup = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        wpmGroup.Controls.Add(wpmCheck);
        wpmGroup.Controls.Add(wpmInput);
        row.Controls.Add(wpmGroup);
        return row;
    }

    static Label FieldLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(8, 6, 4, 0),
    };

    void RegisterInitialHotkey()
    {
        var binding = HotkeyBinding.TryParse(settings.Hotkey, out var parsed) ? parsed : HotkeyBinding.Default;
        settings.Hotkey = binding.ToString();

        if (registrar.TryRegister(binding))
        {
            hotkeyBox.Text = binding.ToString();
            SetStatus("Ready");
        }
        else
        {
            hotkeyBox.Text = "(none)";
            SetStatus($"Hotkey {binding} is in use — click the hotkey box and pick another");
        }
    }

    void OnHotkeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        // Plain Tab still moves focus out of the box.
        if (e.KeyCode == Keys.Tab && !e.Control && !e.Alt) return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        if (e.KeyCode is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;

        var modifiers = HotkeyModifiers.None;
        if (e.Control) modifiers |= HotkeyModifiers.Control;
        if (e.Alt) modifiers |= HotkeyModifiers.Alt;
        if (e.Shift) modifiers |= HotkeyModifiers.Shift;
        if (IsKeyDown(VkLeftWin) || IsKeyDown(VkRightWin)) modifiers |= HotkeyModifiers.Win;

        var candidate = new HotkeyBinding(modifiers, e.KeyCode);
        if (!candidate.IsValid)
        {
            SetStatus("That hotkey can't be used — hold Ctrl, Alt, Shift or Win, or use an F-key");
            return;
        }

        if (!registrar.TryRegister(candidate))
        {
            SetStatus($"Hotkey {candidate} is in use — pick another");
            return;
        }

        hotkeyBox.Text = candidate.ToString();
        settings.Hotkey = candidate.ToString();
        SaveSettings();
        SetStatus("Ready");
    }

    async void OnHotkeyPressed()
    {
        if (job is not null)
        {
            job.Cancel();
            return;
        }

        if (textBox.TextLength == 0)
        {
            SetStatus("Nothing to type");
            return;
        }

        if (IsOwnWindowForeground())
        {
            SetStatus("Click into the target window first");
            return;
        }

        // Taken now, so editing the box or changing settings mid-run does not affect this job.
        var text = textBox.Text;
        var mode = CurrentMode;
        var pace = UsingWpm
            ? TypingPace.WordsPerMinute((int)wpmInput.Value)
            : TypingPace.AfterEach((int)delayInput.Value);
        var modifiers = registrar.Current!.Modifiers;

        var engine = new TypingEngine(keySender);
        engine.Progress += (sent, total) =>
        {
            unitsSent = sent;
            unitsTotal = total;
        };

        using var cts = new CancellationTokenSource();
        job = cts;
        unitsSent = 0;
        unitsTotal = 0;
        SetStatus("Typing…");
        progressTimer.Start();
        try
        {
            var result = await Task.Run(async () =>
            {
                await WaitForModifiersReleased(modifiers, cts.Token);
                return await engine.RunAsync(text, mode, pace, cts.Token);
            });

            SetStatus(result switch
            {
                TypingResult.Completed => "Done",
                TypingResult.Cancelled => "Stopped",
                _ => "Typing failed — the target window did not accept input",
            });
        }
        catch (OperationCanceledException)
        {
            SetStatus("Stopped");
        }
        catch (Exception ex)
        {
            SetStatus($"Error: {ex.Message}");
        }
        finally
        {
            progressTimer.Stop();
            job = null;
        }
    }

    // The hotkey's own modifiers are usually still held when it fires; typing while Ctrl or Alt
    // is down would turn the first characters into shortcuts. Gives up after a few seconds.
    static async Task WaitForModifiersReleased(HotkeyModifiers modifiers, CancellationToken ct)
    {
        var keys = new List<int>();
        if (modifiers.HasFlag(HotkeyModifiers.Control)) keys.Add(VkControl);
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) keys.Add(VkAlt);
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) keys.Add(VkShift);
        if (modifiers.HasFlag(HotkeyModifiers.Win))
        {
            keys.Add(VkLeftWin);
            keys.Add(VkRightWin);
        }

        var deadline = Environment.TickCount64 + ModifierReleaseTimeoutMs;
        while (keys.Any(IsKeyDown) && Environment.TickCount64 < deadline)
            await Task.Delay(10, ct);
    }

    static bool IsKeyDown(int virtualKey) => (NativeMethods.GetAsyncKeyState(virtualKey) & 0x8000) != 0;

    static bool IsOwnWindowForeground()
    {
        NativeMethods.GetWindowThreadProcessId(NativeMethods.GetForegroundWindow(), out var processId);
        return processId == Environment.ProcessId;
    }

    void ShowProgress()
    {
        if (unitsTotal > 0)
            SetStatus($"Typing… {unitsSent:N0}/{unitsTotal:N0}");
    }

    void SetStatus(string text) => statusLabel.Text = text;

    void SaveSettings()
    {
        settings.Mode = CurrentMode;
        settings.DelayMs = (int)delayInput.Value;
        settings.UseWpm = wpmCheck.Checked;
        settings.Wpm = (int)wpmInput.Value;
        if (!settings.TrySave(AppPaths.SettingsPath, out var error))
            SetStatus($"Could not save settings: {error}");
    }

    void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        job?.Cancel();
        SaveSettings();
        progressTimer.Dispose();
        registrar.Dispose();
    }
}
