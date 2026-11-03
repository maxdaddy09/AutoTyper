using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace AutoTyper.Mac;

// The controls are wired by hand rather than bound: the view model raises PropertyChanged on whichever
// thread sets a property, and this class is where that is moved onto the UI thread. Everything that
// touches the view model's UI-thread-only methods (hotkey change, Dispose) runs here on that thread too.
public partial class MainWindow : Window
{
    // Not a key at all. MacHotkey.IsValid rejects it, so TryChangeHotkey shows its own "That hotkey
    // can't be used" message for a key that has no MacKey, without this class repeating the text.
    const MacKey UnsupportedKey = (MacKey)ushort.MaxValue;

    readonly MainViewModel viewModel;
    readonly DispatcherTimer progressTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    // True while the view model's values are being copied into the controls, so that does not echo back.
    bool syncing;
    bool closed;

    public MainWindow()
    {
        InitializeComponent();

        viewModel = Program.CreateViewModel(() => IsActive);

        // Tunnelling, so the box sees the key before its own text handling does.
        hotkeyBox.AddHandler(KeyDownEvent, OnHotkeyBoxKeyDown, RoutingStrategies.Tunnel);
        textBox.TextChanged += (_, _) => viewModel.Text = textBox.Text ?? "";
        wordsRadio.IsCheckedChanged += OnModeChanged;
        wpmCheck.IsCheckedChanged += (_, _) =>
        {
            if (!syncing) viewModel.UseWpm = wpmCheck.IsChecked == true;
        };
        delayInput.ValueChanged += (_, e) => OnNumberChanged(e.NewValue, value => viewModel.DelayMs = value, SyncDelay);
        wpmInput.ValueChanged += (_, e) => OnNumberChanged(e.NewValue, value => viewModel.Wpm = value, SyncWpm);
        permissionButton.Click += (_, _) => viewModel.OpenPermissionSettings();
        progressTimer.Tick += (_, _) => viewModel.RefreshProgress();

        // Subscribed before Start(), so the first status and hotkey reach the controls.
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SyncAll();
        viewModel.Start();
    }

    protected override void OnClosed(EventArgs e)
    {
        closed = true;
        progressTimer.Stop();
        viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        viewModel.Dispose();
        base.OnClosed(e);
    }

    // --- view model -> controls -----------------------------------------------------------------

    void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        var name = e.PropertyName;
        if (Dispatcher.UIThread.CheckAccess()) Sync(name);
        else Dispatcher.UIThread.Post(() => Sync(name));
    }

    void SyncAll() => Sync(null);

    // A null or empty name means everything changed.
    void Sync(string? name)
    {
        if (closed) return;

        var all = string.IsNullOrEmpty(name);
        var wasSyncing = syncing;
        syncing = true;
        try
        {
            if (all || name == nameof(MainViewModel.Mode)) SyncMode();
            if (all || name == nameof(MainViewModel.DelayMs)) SyncDelay();
            if (all || name == nameof(MainViewModel.UseWpm)) wpmCheck.IsChecked = viewModel.UseWpm;
            if (all || name == nameof(MainViewModel.Wpm)) SyncWpm();
            if (all || name == nameof(MainViewModel.WpmAvailable)) wpmCheck.IsEnabled = viewModel.WpmAvailable;
            if (all || name == nameof(MainViewModel.WpmEnabled)) wpmInput.IsEnabled = viewModel.WpmEnabled;
            if (all || name == nameof(MainViewModel.DelayEnabled)) delayInput.IsEnabled = viewModel.DelayEnabled;
            if (all || name == nameof(MainViewModel.HotkeyText)) hotkeyBox.Text = viewModel.HotkeyText;
            if (all || name == nameof(MainViewModel.Status)) statusLabel.Text = viewModel.Status;
            if (all || name == nameof(MainViewModel.NeedsPermission)) permissionButton.IsVisible = viewModel.NeedsPermission;
            if (all || name == nameof(MainViewModel.IsTyping)) SyncTimer();
        }
        finally
        {
            syncing = wasSyncing;
        }
    }

    void SyncMode()
    {
        lettersRadio.IsChecked = viewModel.Mode == TypingMode.Letters;
        wordsRadio.IsChecked = viewModel.Mode == TypingMode.Words;
    }

    void SyncDelay() => SetNumber(delayInput, viewModel.DelayMs);

    void SyncWpm() => SetNumber(wpmInput, viewModel.Wpm);

    // Also used to snap a box back to the view model's value after it was cleared or out of range.
    void SetNumber(NumericUpDown box, int value)
    {
        var wasSyncing = syncing;
        syncing = true;
        try
        {
            if (box.Value != value) box.Value = value;
        }
        finally
        {
            syncing = wasSyncing;
        }
    }

    // The progress text is only refreshed while a job runs.
    void SyncTimer()
    {
        if (viewModel.IsTyping) progressTimer.Start();
        else progressTimer.Stop();
    }

    // --- controls -> view model -----------------------------------------------------------------

    void OnModeChanged(object? sender, RoutedEventArgs e)
    {
        if (!syncing) viewModel.Mode = wordsRadio.IsChecked == true ? TypingMode.Words : TypingMode.Letters;
    }

    void OnNumberChanged(decimal? newValue, Action<int> apply, Action restore)
    {
        if (syncing) return;

        // An emptied box has no number; show the stored one again.
        if (newValue is null) restore();
        else apply((int)Math.Round(newValue.Value));
    }

    // --- hotkey capture -------------------------------------------------------------------------

    void OnHotkeyBoxKeyDown(object? sender, KeyEventArgs e)
    {
        // Plain Tab (and Shift+Tab) still move focus out of the box.
        const KeyModifiers HotkeyModifiers = KeyModifiers.Control | KeyModifiers.Alt | KeyModifiers.Meta;
        if (e.Key == Key.Tab && (e.KeyModifiers & HotkeyModifiers) == 0) return;

        e.Handled = true;

        // Pressing the modifiers one by one on the way to the combination is not a choice yet.
        if (IsModifierKey(e)) return;

        var modifiers = MacModifiers.None;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control)) modifiers |= MacModifiers.Control;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt)) modifiers |= MacModifiers.Option;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) modifiers |= MacModifiers.Shift;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Meta)) modifiers |= MacModifiers.Command;

        // The physical key, not the character: the hotkey is a position on the keyboard, so it means
        // the same thing on any layout.
        var key = MacKeyMap.FromAvalonia(e.PhysicalKey) ?? UnsupportedKey;
        viewModel.TryChangeHotkey(new MacHotkey(modifiers, key));
    }

    static bool IsModifierKey(KeyEventArgs e) =>
        e.PhysicalKey is PhysicalKey.ShiftLeft or PhysicalKey.ShiftRight
            or PhysicalKey.ControlLeft or PhysicalKey.ControlRight
            or PhysicalKey.AltLeft or PhysicalKey.AltRight
            or PhysicalKey.MetaLeft or PhysicalKey.MetaRight
        || e.Key is Key.LeftShift or Key.RightShift
            or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin;
}
