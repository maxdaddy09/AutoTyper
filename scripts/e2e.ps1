# End-to-end check: drives the real AutoTyper.exe through UI Automation and the real global
# hotkey, and reads back what arrives in the target window. Don't touch the keyboard or mouse
# while it runs.
#
#   powershell -File scripts\e2e.ps1 -ExePath <path to AutoTyper.exe>
#
# Prints PASS <scenario> or FAIL <scenario>: <detail>; exits non-zero if anything failed.
#
# -Only runs just the named scenarios, e.g. -Only letters,words
#
# -TargetApp picks the window that receives the typing:
#   Box     (default) a plain multi-line text box in a scratch PowerShell window.
#   Notepad Windows 11 Notepad on a scratch file in %TEMP%. Its built-in spell check / autocorrect
#           rewrites misspelled words typed by any program ("teh qiuck" arrives as "teh iiuck"),
#           so only text made of correctly spelled words and plain ASCII is reliable there.
#           The script refuses to run if you already have other Notepad windows open.
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [string[]]$Only,
    [ValidateSet('Box', 'Notepad')][string]$TargetApp = 'Box'
)

$ErrorActionPreference = 'Stop'
$ExePath = (Resolve-Path $ExePath).Path

# An AutoTyper that is already open owns the Ctrl+Alt+T hotkey: the copies started here could not
# register it, and the open one would answer the test's key presses and type its own text.
if (Get-Process AutoTyper -ErrorAction SilentlyContinue) {
    throw 'AutoTyper is already running. Close it first: it holds the Ctrl+Alt+T hotkey the tests use.'
}
# With -File, "-Only a,b" arrives as the single string "a,b".
if ($Only) { $Only = @($Only | ForEach-Object { $_ -split ',' }) }
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class E2E {
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, System.Text.StringBuilder lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, string lParam);

    // WM_GETTEXTLENGTH / WM_GETTEXT / WM_SETTEXT on a classic edit control.
    public static string GetText(IntPtr hWnd) {
        int length = (int)SendMessage(hWnd, 0x000E, IntPtr.Zero, IntPtr.Zero);
        var text = new System.Text.StringBuilder(length + 1);
        SendMessage(hWnd, 0x000D, (IntPtr)(length + 1), text);
        return text.ToString();
    }
    public static void SetText(IntPtr hWnd, string text) { SendMessage(hWnd, 0x000C, IntPtr.Zero, text); }

    public static uint PidOf(IntPtr hWnd) { uint pid; GetWindowThreadProcessId(hWnd, out pid); return pid; }

    public static IntPtr FindVisibleByTitle(string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            var text = new System.Text.StringBuilder(256);
            GetWindowText(h, text, 256);
            if (text.ToString() != title) return true;
            found = h;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    public static void Combo(params byte[] keys) {
        foreach (var k in keys) keybd_event(k, 0, 0, UIntPtr.Zero);
        System.Threading.Thread.Sleep(80);
        for (int i = keys.Length - 1; i >= 0; i--) keybd_event(keys[i], 0, 2, UIntPtr.Zero);
    }
    public static uint ForegroundPid() { uint pid; GetWindowThreadProcessId(GetForegroundWindow(), out pid); return pid; }
    public static void Bring(IntPtr hWnd, bool altTap) {
        if (altTap) { keybd_event(0x12, 0, 0, UIntPtr.Zero); keybd_event(0x12, 0, 2, UIntPtr.Zero); }
        SetForegroundWindow(hWnd);
    }
}
'@

$AE = [System.Windows.Automation.AutomationElement]
$ValuePattern = [System.Windows.Automation.ValuePattern]::Pattern
$TextPattern = [System.Windows.Automation.TextPattern]::Pattern
$SelectionItemPattern = [System.Windows.Automation.SelectionItemPattern]::Pattern
$settingsPath = Join-Path $env:APPDATA 'AutoTyper\settings.json'
$scratchDir = Join-Path $env:TEMP 'autotyper-e2e'
$targetFile = Join-Path $scratchDir 'target.txt'
$hotkeyT = [byte[]](0x11, 0x12, 0x54)   # Ctrl+Alt+T, the default
$hotkeyY = [byte[]](0x11, 0x12, 0x59)   # Ctrl+Alt+Y
$script:failed = 0
$script:ran = 0

# The scenarios start from, and change, the real settings file, so any existing one is put back
# (or the file removed again if there was none) when the script ends.
$settingsBackup = Join-Path $scratchDir 'settings.backup.json'
$hadSettings = Test-Path $settingsPath
if ($hadSettings) {
    New-Item -ItemType Directory -Force $scratchDir | Out-Null
    Copy-Item $settingsPath $settingsBackup -Force
}

# --- results --------------------------------------------------------------------------------

# A scenario's body returns nothing on success, or a string describing what went wrong.
function Check([string]$name, [scriptblock]$body) {
    if ($Only -and $Only -notcontains $name) { return }
    $script:ran++
    try { $problem = & $body } catch { $problem = "$($_.Exception.Message) [line $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim())]" }
    if ($problem) {
        $detail = "$problem"
        if ($detail.Length -gt 400) { $detail = $detail.Substring(0, 400) + '...' }
        Write-Output "FAIL ${name}: $detail"
        $script:failed++
    } else {
        Write-Output "PASS $name"
    }
}

function Normalize([string]$s) { $s -replace "`r`n?", "`n" }

# --- AutoTyper ------------------------------------------------------------------------------

function Prop($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

function Start-App([bool]$fresh) {
    if ($fresh) { Remove-Item $settingsPath -ErrorAction SilentlyContinue }
    $p = Start-Process $ExePath -PassThru
    for ($i = 0; $i -lt 40 -and $p.MainWindowHandle -eq [IntPtr]::Zero; $i++) { Start-Sleep -Milliseconds 250; $p.Refresh() }
    if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw 'AutoTyper window did not appear' }
    $script:app = $p
    $script:appWin = $AE::FromHandle($p.MainWindowHandle)
    Start-Sleep -Milliseconds 400
}

function Stop-App {
    if ($script:app -and -not $script:app.HasExited) {
        [void]$script:app.CloseMainWindow()
        if (-not $script:app.WaitForExit(5000)) { $script:app.Kill() }
    }
}

function Ctl([string]$id) { $script:appWin.FindFirst('Descendants', (Prop $AE::AutomationIdProperty $id)) }
function Get-Status { (Ctl 'statusLabel').Current.Name }
function Get-BoxText([string]$id) { (Ctl $id).GetCurrentPattern($ValuePattern).Current.Value }
# A number box is a spinner whose text field is its only Edit child.
function Spin-Edit([string]$id) { (Ctl $id).FindFirst('Descendants', (Prop $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::Edit))) }
function Delay-Edit { Spin-Edit 'delayInput' }
function Get-AppWpm { (Spin-Edit 'wpmInput').GetCurrentPattern($ValuePattern).Current.Value }
function Test-AppEnabled([string]$id) { (Ctl $id).Current.IsEnabled }
function Test-WpmChecked { (Ctl 'wpmCheck').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On }
function Get-AppDelay { Delay-Edit | ForEach-Object { $_.GetCurrentPattern($ValuePattern).Current.Value } }
function Test-RadioSelected([string]$id) { (Ctl $id).GetCurrentPattern($SelectionItemPattern).Current.IsSelected }

function Set-AppText([string]$text) { (Ctl 'textBox').GetCurrentPattern($ValuePattern).SetValue($text) }

function Set-AppMode([string]$mode) {
    $id = if ($mode -eq 'Words') { 'wordsRadio' } else { 'lettersRadio' }
    (Ctl $id).GetCurrentPattern($SelectionItemPattern).Select()
}

# Turns the words-per-minute option on or off; the number box is greyed out while it is off, so
# the value is only set when turning it on.
function Set-AppWpm([bool]$on, [int]$wpm = 60) {
    $toggle = (Ctl 'wpmCheck').GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
    if ((Test-WpmChecked) -ne $on) { $toggle.Toggle() }
    if ($on) {
        (Spin-Edit 'wpmInput').GetCurrentPattern($ValuePattern).SetValue("$wpm")
        (Ctl 'textBox').SetFocus()
        Start-Sleep -Milliseconds 300
    }
}

function Set-AppDelay([int]$ms) {
    (Delay-Edit).GetCurrentPattern($ValuePattern).SetValue("$ms")
    (Ctl 'textBox').SetFocus()   # leaving the number box is what commits the value
    Start-Sleep -Milliseconds 300
}

function Wait-Status([string]$pattern, [double]$timeoutSec) {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    $last = ''
    while ($timer.Elapsed.TotalSeconds -lt $timeoutSec) {
        $last = Get-Status
        if ($last -match $pattern) { return $last }
        Start-Sleep -Milliseconds 40
    }
    throw "timed out after ${timeoutSec}s waiting for status /$pattern/ (last status: '$last')"
}

# --- target window --------------------------------------------------------------------------

$boxScript = Join-Path $scratchDir 'target-box.ps1'
$boxTitle = 'AutoTyper e2e target'

# A plain multi-line text box, hosted by a scratch PowerShell process.
$boxSource = @'
Add-Type -AssemblyName System.Windows.Forms
$form = New-Object System.Windows.Forms.Form
$form.Text = 'AutoTyper e2e target'
$form.StartPosition = 'Manual'
$form.Location = New-Object System.Drawing.Point(120, 120)
$form.Size = New-Object System.Drawing.Size(700, 500)
$box = New-Object System.Windows.Forms.TextBox
$box.Name = 'targetBox'
$box.Multiline = $true
$box.ScrollBars = 'Vertical'
$box.MaxLength = [int]::MaxValue
$box.Dock = 'Fill'
$form.Controls.Add($box)
$form.Add_Shown({ $box.Focus() })
[System.Windows.Forms.Application]::Run($form)
'@

# Notepad puts a '*' in front of the title once the text has been modified.
function Find-Target {
    if ($TargetApp -eq 'Notepad') {
        return Get-Process notepad -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowTitle -like '*target.txt*' } | Select-Object -First 1
    }
    # PowerShell's own MainWindowHandle does not see the form of a console-less PowerShell host.
    $handle = [E2E]::FindVisibleByTitle($boxTitle)
    if ($handle -eq [IntPtr]::Zero) { return $null }
    [pscustomobject]@{ Id = [E2E]::PidOf($handle); MainWindowHandle = $handle }
}

function Start-Target {
    New-Item -ItemType Directory -Force $scratchDir | Out-Null
    if ($TargetApp -eq 'Notepad') {
        $others = Get-Process notepad -ErrorAction SilentlyContinue |
            Where-Object { $_.MainWindowTitle -and $_.MainWindowTitle -notlike '*target.txt*' }
        if ($others) { throw 'Close your other Notepad windows first; this test needs a Notepad of its own.' }
        Stop-Target
        Set-Content -Path $targetFile -Value '' -Encoding UTF8
        Start-Process notepad.exe -ArgumentList "`"$targetFile`""
    } else {
        Stop-Target
        Set-Content -Path $boxScript -Value $boxSource -Encoding UTF8
        # -WindowStyle Hidden is PowerShell's own argument and hides only its console; passing it
        # to Start-Process instead would hide the form as well.
        Start-Process powershell -ArgumentList '-STA', '-NoProfile', '-WindowStyle', 'Hidden', '-ExecutionPolicy', 'Bypass', '-File', "`"$boxScript`""
    }

    $proc = $null
    for ($i = 0; $i -lt 60 -and -not $proc; $i++) { Start-Sleep -Milliseconds 250; $proc = Find-Target }
    if (-not $proc) { throw "$TargetApp window did not appear" }
    $script:tgtProc = $proc
    $script:tgtWin = $AE::FromHandle($proc.MainWindowHandle)
    # Windows PowerShell's .NET Framework form publishes no control names or patterns to UI
    # Automation, so the box has a single anonymous descendant and is read through Win32 messages.
    $condition = if ($TargetApp -eq 'Notepad') {
        Prop $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::Document)
    } else {
        [System.Windows.Automation.Condition]::TrueCondition
    }
    $script:tgtDoc = $null
    for ($i = 0; $i -lt 20 -and -not $script:tgtDoc; $i++) {
        $script:tgtDoc = $script:tgtWin.FindFirst('Descendants', $condition)
        if (-not $script:tgtDoc) { Start-Sleep -Milliseconds 250 }
    }
    if ($script:tgtDoc) { $script:tgtEdit = [IntPtr][int]$script:tgtDoc.Current.NativeWindowHandle }
    if (-not $script:tgtDoc) { throw "$TargetApp text area not found" }
}

function Stop-Target {
    $proc = Find-Target
    if ($proc) { Stop-Process -Id $proc.Id -Force }
}

function Clear-Target {
    if ($TargetApp -eq 'Notepad') { $script:tgtDoc.GetCurrentPattern($ValuePattern).SetValue('') }
    else { [E2E]::SetText($script:tgtEdit, '') }
}

function Read-Target {
    $text = if ($TargetApp -eq 'Notepad') {
        $script:tgtDoc.GetCurrentPattern($TextPattern).DocumentRange.GetText(-1)
    } else {
        [E2E]::GetText($script:tgtEdit)
    }
    (Normalize $text).TrimEnd("`n")
}

# Windows may refuse a plain SetForegroundWindow; tapping Alt first lifts that restriction.
function Focus-Window($process, $element) {
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        [E2E]::Bring($process.MainWindowHandle, ($attempt -gt 0))
        for ($i = 0; $i -lt 10; $i++) {
            if ([E2E]::ForegroundPid() -eq $process.Id) {
                # The scratch box rejects UIA focus but already has it (its form focuses it on open).
                try { $element.SetFocus() } catch { }
                Start-Sleep -Milliseconds 200
                return
            }
            Start-Sleep -Milliseconds 100
        }
    }
    throw "could not bring process $($process.Id) to the foreground"
}

function Focus-Target { Focus-Window $script:tgtProc $script:tgtDoc }
function Focus-App { Focus-Window $script:app $script:appWin }

# Refuses to inject anything unless the target window has the foreground, so a stray hotkey or the typed
# text can never land in another application.
function Press-Hotkey([byte[]]$keys) {
    if ([E2E]::ForegroundPid() -ne $script:tgtProc.Id) { throw 'The target window is not in the foreground; not sending the hotkey' }
    [E2E]::Combo($keys)
}

# Sets the controls, types the text into the target with the default hotkey, and waits until
# the target shows exactly the expected text.
function Invoke-Typing([string]$text, [string]$mode, [int]$delayMs, [double]$timeoutSec = 30, [int]$wpm = 0) {
    Set-AppMode $mode
    if ($wpm -gt 0) { Set-AppWpm $true $wpm }   # replaces the delay box, which is then greyed out
    else { Set-AppDelay $delayMs }
    Set-AppText $text
    Clear-Target
    Focus-Target
    $expected = (Normalize $text).TrimEnd("`n")
    $timer = [Diagnostics.Stopwatch]::StartNew()
    Press-Hotkey $hotkeyT
    $status = Wait-Status '^(Done|Stopped|Typing failed|Error)' $timeoutSec
    $elapsed = $timer.ElapsedMilliseconds
    $actual = Read-Target
    for ($i = 0; $i -lt 100 -and $actual -ne $expected; $i++) { Start-Sleep -Milliseconds 100; $actual = Read-Target }
    [pscustomobject]@{ Status = $status; Text = $actual; Expected = $expected; ElapsedMs = $elapsed }
}

# Console encodings mangle non-ASCII text, so short strings are shown as UTF-16 code units.
function Describe-Mismatch($result) {
    $hex = { param($s) if ($s.Length -le 60) { ($s.ToCharArray() | ForEach-Object { '{0:X4}' -f [int]$_ }) -join ' ' } else { '(long)' } }
    "status '$($result.Status)'; expected $($result.Expected.Length) chars [$(& $hex $result.Expected)], got $($result.Text.Length) chars [$(& $hex $result.Text)]"
}

# --- scenarios ------------------------------------------------------------------------------

$e = [char]0xE9; $o = [char]0xF6; $smile = [char]::ConvertFromUtf32(0x1F600)
$sample = "H${e}llo w${o}rld $smile`nsecond line"
$sampleLetterUnits = ([System.Globalization.StringInfo]::new($sample)).LengthInTextElements

Start-Target
try {
    Check 'limit' {
        Start-App $true
        try {
            $handle = [IntPtr](Ctl 'textBox').Current.NativeWindowHandle
            $limit = [E2E]::SendMessage($handle, 0xD5, [IntPtr]::Zero, [IntPtr]::Zero).ToInt64()   # EM_GETLIMITTEXT
            if ($limit -lt 1000000) { return "text box accepts only $limit characters; a large paste would be cut off" }
        } finally { Stop-App }
    }

    Check 'letters' {
        Start-App $true
        try {
            $r = Invoke-Typing $sample 'Letters' 20
            if ($r.Text -ne $r.Expected) { return Describe-Mismatch $r }
            $minMs = 0.9 * ($sampleLetterUnits - 1) * 20
            if ($r.ElapsedMs -lt $minMs) { return "finished in $($r.ElapsedMs) ms; 20 ms between $sampleLetterUnits letters needs at least $minMs ms" }
        } finally { Stop-App }
    }

    Check 'words' {
        Start-App $true
        try {
            $r = Invoke-Typing $sample 'Words' 200
            if ($r.Text -ne $r.Expected) { return Describe-Mismatch $r }
            $wordUnits = 5   # "Hello ", "world ", emoji + newline, "second ", "line"
            $minMs = 0.9 * ($wordUnits - 1) * 200
            if ($r.ElapsedMs -lt $minMs) { return "finished in $($r.ElapsedMs) ms; 200 ms between $wordUnits words needs at least $minMs ms" }
        } finally { Stop-App }
    }

    Check 'crlf' {
        Start-App $true
        try {
            $r = Invoke-Typing "line1`r`nline2`r`nline3" 'Letters' 5
            if ($r.Text -ne "line1`nline2`nline3") { return Describe-Mismatch $r }
        } finally { Stop-App }
    }

    # 300 words per minute is a word every 200 ms. The pace is a target, so the time spent typing
    # a word counts toward its slot: the run takes about 11 x 200 ms plus the last word, not 11 x
    # (200 ms + typing time). Checking both ends tells the two apart on a machine that types a
    # six-character word in under about 100 ms.
    Check 'wpm' {
        Start-App $true
        try {
            $words = 'alpha bravo delta gamma sigma theta omega kappa lambda zeta sierra tango'
            $r = Invoke-Typing $words 'Words' 0 30 300
            if ($r.Text -ne $r.Expected) { return Describe-Mismatch $r }
            $minMs = 0.95 * 11 * 200
            $maxMs = 11 * 200 + 700
            if ($r.ElapsedMs -lt $minMs) { return "12 words at 300 wpm took only $($r.ElapsedMs) ms; at least $minMs ms expected" }
            if ($r.ElapsedMs -gt $maxMs) { return "12 words at 300 wpm took $($r.ElapsedMs) ms; about 2200 ms plus the last word expected, at most $maxMs" }
        } finally { Stop-App }
    }

    Check 'wpm-controls' {
        Start-App $true
        try {
            $state = { "check=$(Test-AppEnabled 'wpmCheck') wpm=$(Test-AppEnabled 'wpmInput') delay=$(Test-AppEnabled 'delayInput')" }
            if ((& $state) -ne 'check=False wpm=False delay=True') { return "Letters mode: $(& $state)" }
            Set-AppMode 'Words'
            Start-Sleep -Milliseconds 200
            if ((& $state) -ne 'check=True wpm=False delay=True') { return "Words mode, option off: $(& $state)" }
            Set-AppWpm $true 90
            if ((& $state) -ne 'check=True wpm=True delay=False') { return "Words mode, option on: $(& $state)" }
            Set-AppMode 'Letters'
            Start-Sleep -Milliseconds 200
            if ((& $state) -ne 'check=False wpm=False delay=True') { return "back in Letters mode: $(& $state)" }
        } finally { Stop-App }
    }

    Check 'stop' {
        Start-App $true
        try {
            $text = 'lorem ipsum dolor sit amet ' * 4000   # about 108,000 characters at 1 ms each
            Set-AppMode 'Letters'
            Set-AppDelay 1
            Set-AppText $text
            Clear-Target
            Focus-Target
            Press-Hotkey $hotkeyT
            [void](Wait-Status '^Typing' 5)
            Start-Sleep -Seconds 1
            $timer = [Diagnostics.Stopwatch]::StartNew()
            Press-Hotkey $hotkeyT
            $status = Wait-Status '^(Stopped|Done|Typing failed|Error)' 5
            $stopMs = $timer.ElapsedMilliseconds
            Start-Sleep -Seconds 2   # let the target drain keystrokes already sent
            $typed = Read-Target
            if ($status -ne 'Stopped') { return "status after the second hotkey press was '$status'" }
            if ($stopMs -gt 1000) { return "took $stopMs ms to stop" }
            if ($typed.Length -eq 0 -or $typed.Length -ge $text.Length) { return "expected a partial result, got $($typed.Length) of $($text.Length) characters" }
            if (-not $text.StartsWith($typed)) { return 'what was typed is not a prefix of the text' }
            $before = $typed.Length
            Start-Sleep -Seconds 2
            if ((Read-Target).Length -ne $before) { return 'typing continued after the stop' }
        } finally { Stop-App }
    }

    # A job with no delay that runs for tens of seconds: the window must stay responsive the whole
    # time and every character must arrive. Keystroke injection is slow on some machines (about
    # 90 characters a second here, measured with a bare SendInput loop), so the text is kept short;
    # huge pastes are covered by 'limit' (the box accepts them), the chunker's timing test and 'stop'.
    Check 'long-run' {
        Start-App $true
        try {
            $text = "lorem ipsum dolor sit amet consectetur`n" * 80   # 3,120 characters
            Set-AppMode 'Words'
            Set-AppDelay 0
            Set-AppText $text
            Clear-Target
            Focus-Target
            Press-Hotkey $hotkeyT
            $worstMs = 0
            $overall = [Diagnostics.Stopwatch]::StartNew()
            while ($true) {
                $probe = [Diagnostics.Stopwatch]::StartNew()
                $status = Get-Status
                $worstMs = [Math]::Max($worstMs, $probe.ElapsedMilliseconds)
                if ($status -match '^(Done|Stopped|Typing failed|Error)') { break }
                if ($overall.Elapsed.TotalSeconds -gt 180) { return "still '$status' after 180 s" }
                Start-Sleep -Milliseconds 200
            }
            if ($status -ne 'Done') { return "finished with status '$status'" }
            if ($worstMs -gt 1000) { return "window was unresponsive for $worstMs ms while typing" }
            $expected = $text.TrimEnd("`n")
            $actual = Read-Target
            for ($i = 0; $i -lt 1200 -and $actual -ne $expected; $i++) { Start-Sleep -Milliseconds 100; $actual = Read-Target }
            if ($actual -ne $expected) { return "the target ended with $($actual.Length) of $($expected.Length) characters" }
        } finally { Stop-App }
    }

    Check 'restart' {
        Start-App $true
        try {
            Focus-App
            (Ctl 'hotkeyBox').SetFocus()
            Start-Sleep -Milliseconds 200
            [E2E]::Combo($hotkeyY)
            Start-Sleep -Milliseconds 500
            if ((Get-BoxText 'hotkeyBox') -ne 'Ctrl+Alt+Y') { return "hotkey box shows '$(Get-BoxText 'hotkeyBox')' after choosing Ctrl+Alt+Y; status '$(Get-Status)'" }
            Set-AppMode 'Words'
            Set-AppDelay 123
            Set-AppWpm $true 90
            Set-AppText 'ok'
            Clear-Target
            Focus-Target
            Press-Hotkey $hotkeyY
            [void](Wait-Status '^Done' 10)
            $typed = Read-Target
            for ($i = 0; $i -lt 30 -and $typed -ne 'ok'; $i++) { Start-Sleep -Milliseconds 100; $typed = Read-Target }
            if ($typed -ne 'ok') { return "the new hotkey did not type: the target has '$typed'" }
        } finally { Stop-App }

        Start-App $false
        try {
            if ((Get-BoxText 'hotkeyBox') -ne 'Ctrl+Alt+Y') { return "after restart the hotkey box shows '$(Get-BoxText 'hotkeyBox')'" }
            if (-not (Test-RadioSelected 'wordsRadio')) { return 'after restart Words is not selected' }
            if ((Get-AppDelay) -ne '123') { return "after restart the delay is '$(Get-AppDelay)'" }
            if (-not (Test-WpmChecked)) { return 'after restart the words-per-minute option is off' }
            if ((Get-AppWpm) -ne '90') { return "after restart the words per minute is '$(Get-AppWpm)'" }
            if (-not (Test-AppEnabled 'wpmInput')) { return 'after restart the words-per-minute box is greyed out' }
            if ((Get-Status) -ne 'Ready') { return "after restart the status is '$(Get-Status)'" }
        } finally { Stop-App }
    }
} finally {
    Stop-Target
    if ($hadSettings) { Copy-Item $settingsBackup $settingsPath -Force }
    else { Remove-Item $settingsPath -ErrorAction SilentlyContinue }
}

if ($script:ran -eq 0) { Write-Output 'No scenarios ran (check the -Only names)'; exit 1 }
if ($script:failed -gt 0) { Write-Output "$($script:failed) of $($script:ran) scenario(s) failed"; exit 1 }
Write-Output "All $($script:ran) scenario(s) passed"
