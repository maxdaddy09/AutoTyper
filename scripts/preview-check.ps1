# Preview check: drives the real AutoTyper.Mac window (the Avalonia app, running in preview mode on
# Windows) through UI Automation, and presses one key combination in its hotkey box. Don't touch the
# keyboard or mouse while it runs.
#
#   powershell -File scripts\preview-check.ps1 -ExePath AutoTyper.Mac\bin\Debug\net10.0\AutoTyper.exe
#
# Prints PASS <scenario> or FAIL <scenario>: <detail>; exits non-zero if anything failed.
#
# -Only runs just the named scenarios, e.g. -Only controls,restart
#
# The Mac app's executable is also called AutoTyper.exe, like the Windows app's, and you may have the
# Windows one open. So this script never looks a process up or stops one by name: it starts its own
# copy, remembers that process, finds its window through that process, and stops only that process.
# The preview keeps its settings in %LOCALAPPDATA%\AutoTyper-Mac-Preview\settings.json (never the
# Windows app's %AppData%\AutoTyper); an existing file is put back when the script ends.
param(
    [Parameter(Mandatory = $true)][string]$ExePath,
    [string[]]$Only
)

$ErrorActionPreference = 'Stop'
$ExePath = (Resolve-Path $ExePath).Path
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
$SelectionItemPattern = [System.Windows.Automation.SelectionItemPattern]::Pattern
$TogglePattern = [System.Windows.Automation.TogglePattern]::Pattern
$settingsDir = Join-Path $env:LOCALAPPDATA 'AutoTyper-Mac-Preview'
$settingsPath = Join-Path $settingsDir 'settings.json'
$scratchDir = Join-Path $env:TEMP 'autotyper-preview-check'
$settingsBackup = Join-Path $scratchDir 'settings.backup.json'
$hotkeyY = [byte[]](0x11, 0x12, 0x59)   # Ctrl+Alt+Y
$previewStatus = 'Preview: typing and the hotkey only work on macOS'
$script:failed = 0
$script:ran = 0
$script:app = $null

# The scenarios start from, and change, the preview's settings file, so an existing one is put back
# (or the file, and the folder if it did not exist, removed again) when the script ends.
$hadFolder = Test-Path $settingsDir
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

# --- AutoTyper (the Mac window) -------------------------------------------------------------

function Prop($property, $value) { New-Object System.Windows.Automation.PropertyCondition($property, $value) }

# Starts a copy of the app and remembers its process. The window is found through that process
# only. Windows are never searched for by title or enumerated.
#
# The scenarios call this before their own try/finally, so a start that fails (it throws) must not
# leave the process it started running: it is stopped here, by its process object, before the error
# goes on. A copy still held from an earlier start is stopped first, so none is ever orphaned.
function Start-App([bool]$fresh) {
    Stop-App
    if ($fresh) { Remove-Item $settingsPath -ErrorAction SilentlyContinue }
    $p = Start-Process $ExePath -PassThru
    $script:app = $p
    try {
        # A cold start of an Avalonia app takes a few seconds.
        for ($i = 0; $i -lt 80 -and $p.MainWindowHandle -eq [IntPtr]::Zero -and -not $p.HasExited; $i++) {
            Start-Sleep -Milliseconds 250
            $p.Refresh()
        }
        if ($p.HasExited) { throw "AutoTyper exited at start with code $($p.ExitCode)" }
        if ($p.MainWindowHandle -eq [IntPtr]::Zero) { throw 'AutoTyper window did not appear within 20 s' }
        $script:appWin = $AE::FromHandle($p.MainWindowHandle)
        # The window exists before its controls are published to UI Automation: wait for the status text.
        for ($i = 0; $i -lt 40; $i++) {
            $status = Ctl 'statusLabel'
            if ($status -and $status.Current.Name) { break }
            Start-Sleep -Milliseconds 250
        }
        if (-not (Ctl 'statusLabel')) { throw 'the status label did not appear in the window' }
        Start-Sleep -Milliseconds 300
    } catch {
        Stop-App
        throw
    }
}

# Closes only the process started by Start-App.
function Stop-App {
    if ($script:app -and -not $script:app.HasExited) {
        [void]$script:app.CloseMainWindow()
        if (-not $script:app.WaitForExit(5000)) { $script:app.Kill() }
    }
    $script:app = $null
}

function Ctl([string]$id) { $script:appWin.FindFirst('Descendants', (Prop $AE::AutomationIdProperty $id)) }
function Get-Status { (Ctl 'statusLabel').Current.Name }
function Get-BoxText([string]$id) { (Ctl $id).GetCurrentPattern($ValuePattern).Current.Value }
# A number box is a spinner whose text field is its only Edit child (the two buttons are Buttons).
function Spin-Edit([string]$id) { (Ctl $id).FindFirst('Descendants', (Prop $AE::ControlTypeProperty ([System.Windows.Automation.ControlType]::Edit))) }
function Get-AppDelay { (Spin-Edit 'delayInput').GetCurrentPattern($ValuePattern).Current.Value }
function Get-AppWpm { (Spin-Edit 'wpmInput').GetCurrentPattern($ValuePattern).Current.Value }
function Test-AppEnabled([string]$id) { (Ctl $id).Current.IsEnabled }
function Test-WpmChecked { (Ctl 'wpmCheck').GetCurrentPattern($TogglePattern).Current.ToggleState -eq [System.Windows.Automation.ToggleState]::On }
function Test-RadioSelected([string]$id) { (Ctl $id).GetCurrentPattern($SelectionItemPattern).Current.IsSelected }

function Set-AppMode([string]$mode) {
    $id = if ($mode -eq 'Words') { 'wordsRadio' } else { 'lettersRadio' }
    (Ctl $id).GetCurrentPattern($SelectionItemPattern).Select()
}

# Turns the words-per-minute option on or off; the number box is greyed out while it is off, so
# the value is only set when turning it on.
function Set-AppWpm([bool]$on, [int]$wpm = 60) {
    $toggle = (Ctl 'wpmCheck').GetCurrentPattern($TogglePattern)
    if ((Test-WpmChecked) -ne $on) { $toggle.Toggle() }
    if ($on) {
        Start-Sleep -Milliseconds 200
        (Spin-Edit 'wpmInput').GetCurrentPattern($ValuePattern).SetValue("$wpm")
        (Ctl 'textBox').SetFocus()   # leaving the number box is what commits the value
        Start-Sleep -Milliseconds 300
    }
}

function Set-AppDelay([int]$ms) {
    (Spin-Edit 'delayInput').GetCurrentPattern($ValuePattern).SetValue("$ms")
    (Ctl 'textBox').SetFocus()
    Start-Sleep -Milliseconds 300
}

# Windows may refuse a plain SetForegroundWindow; tapping Alt first lifts that restriction.
function Focus-App {
    for ($attempt = 0; $attempt -lt 2; $attempt++) {
        [E2E]::Bring($script:app.MainWindowHandle, ($attempt -gt 0))
        for ($i = 0; $i -lt 10; $i++) {
            if ([E2E]::ForegroundPid() -eq $script:app.Id) { Start-Sleep -Milliseconds 200; return }
            Start-Sleep -Milliseconds 100
        }
    }
    throw "could not bring process $($script:app.Id) to the foreground"
}

# Presses a key combination, but only while the app's own window has the foreground, so the keys
# can never land in another application.
function Press-InApp([byte[]]$keys) {
    if ([E2E]::ForegroundPid() -ne $script:app.Id) { throw 'the AutoTyper window is not in the foreground; not sending keys' }
    [E2E]::Combo($keys)
}

# The hotkey box is read-only, so the combination is chosen by pressing it while the box has the focus.
function Choose-Hotkey([byte[]]$keys) {
    Focus-App
    (Ctl 'hotkeyBox').SetFocus()
    Start-Sleep -Milliseconds 200
    Press-InApp $keys
    Start-Sleep -Milliseconds 500
}

# --- scenarios ------------------------------------------------------------------------------

try {
    Check 'controls' {
        Start-App $true
        try {
            $missing = @('textBox', 'hotkeyBox', 'lettersRadio', 'wordsRadio', 'delayInput', 'wpmCheck', 'wpmInput', 'statusLabel') |
                Where-Object { -not (Ctl $_) }
            if ($missing) { return "not found in the window: $($missing -join ', ')" }
            # permissionButton is not checked here: it is hidden unless permission is missing (never,
            # in preview), and UI Automation does not publish hidden controls, so this script cannot
            # see it. Its id, default visibility and text are covered by MainWindowXamlTests in
            # AutoTyper.Mac.Tests, and by the macOS checklist.
        } finally { Stop-App }
    }

    Check 'initial-state' {
        Start-App $true
        try {
            if (-not (Test-RadioSelected 'lettersRadio')) { return 'Letters is not selected' }
            if (Test-RadioSelected 'wordsRadio') { return 'Words is selected' }
            if ((Get-AppDelay) -ne '50') { return "delay is '$(Get-AppDelay)', expected 50" }
            if ((Get-BoxText 'hotkeyBox') -ne 'Control+Option+T') { return "hotkey is '$(Get-BoxText 'hotkeyBox')', expected Control+Option+T" }
            if ((Get-Status) -ne $previewStatus) { return "status is '$(Get-Status)'" }
            # Hidden controls are left out of the UI Automation tree, so this fails only if a control with
            # this id is visible. It cannot tell "hidden" from "missing" (see MainWindowXamlTests).
            $button = Ctl 'permissionButton'
            if ($button -and -not $button.Current.IsOffscreen) { return 'permissionButton is visible' }
        } finally { Stop-App }
    }

    Check 'pace-rules' {
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

    Check 'hotkey-capture' {
        Start-App $true
        try {
            Choose-Hotkey $hotkeyY
            if ((Get-BoxText 'hotkeyBox') -ne 'Control+Option+Y') { return "hotkey box shows '$(Get-BoxText 'hotkeyBox')' after pressing Ctrl+Alt+Y; status '$(Get-Status)'" }
        } finally { Stop-App }
    }

    Check 'restart' {
        Start-App $true
        try {
            Choose-Hotkey $hotkeyY
            if ((Get-BoxText 'hotkeyBox') -ne 'Control+Option+Y') { return "hotkey box shows '$(Get-BoxText 'hotkeyBox')' after choosing Ctrl+Alt+Y; status '$(Get-Status)'" }
            Set-AppMode 'Words'
            Set-AppDelay 123
            Set-AppWpm $true 90
        } finally { Stop-App }

        Start-App $false
        try {
            if ((Get-BoxText 'hotkeyBox') -ne 'Control+Option+Y') { return "after restart the hotkey box shows '$(Get-BoxText 'hotkeyBox')'" }
            if (-not (Test-RadioSelected 'wordsRadio')) { return 'after restart Words is not selected' }
            if ((Get-AppDelay) -ne '123') { return "after restart the delay is '$(Get-AppDelay)'" }
            if (-not (Test-WpmChecked)) { return 'after restart the words-per-minute option is off' }
            if ((Get-AppWpm) -ne '90') { return "after restart the words per minute is '$(Get-AppWpm)'" }
            if (-not (Test-AppEnabled 'wpmInput')) { return 'after restart the words-per-minute box is greyed out' }
            if ((Get-Status) -ne $previewStatus) { return "after restart the status is '$(Get-Status)'" }
        } finally { Stop-App }
    }

    # 300,000 characters set through UI Automation's Value pattern (no clipboard): the box must keep all
    # of them, and the window must keep answering UI Automation queries while it takes them in.
    Check 'large-text' {
        Start-App $true
        try {
            $line = 'lorem ipsum dolor sit amet consectetur adipiscing' + "`n"   # 50 characters
            $text = $line * 6000
            if ($text.Length -ne 300000) { return "test text has $($text.Length) characters" }
            $box = (Ctl 'textBox').GetCurrentPattern($ValuePattern)
            $box.SetValue($text)

            $worstMs = 0
            for ($i = 0; $i -lt 5; $i++) {
                $probe = [Diagnostics.Stopwatch]::StartNew()
                [void](Get-Status)
                $worstMs = [Math]::Max($worstMs, $probe.ElapsedMilliseconds)
                Start-Sleep -Milliseconds 200
            }
            if ($worstMs -gt 2000) { return "the window took $worstMs ms to answer a UI Automation query after the text was set" }

            $actual = ($box.Current.Value) -replace "`r`n?", "`n"
            if ($actual.Length -ne $text.Length) { return "the box holds $($actual.Length) of $($text.Length) characters" }
            if ($actual -ne $text) { return 'the box holds 300,000 characters, but not the ones that were set' }
        } finally { Stop-App }
    }
} finally {
    Stop-App
    if ($hadSettings) { Copy-Item $settingsBackup $settingsPath -Force }
    else {
        Remove-Item $settingsPath -ErrorAction SilentlyContinue
        # Only the folder this script's runs created, and only if nothing else is in it.
        if (-not $hadFolder -and (Test-Path $settingsDir) -and -not (Get-ChildItem $settingsDir -Force)) {
            Remove-Item $settingsDir -ErrorAction SilentlyContinue
        }
    }
}

if ($script:ran -eq 0) { Write-Output 'No scenarios ran (check the -Only names)'; exit 1 }
if ($script:failed -gt 0) { Write-Output "$($script:failed) of $($script:ran) scenario(s) failed"; exit 1 }
Write-Output "All $($script:ran) scenario(s) passed"
