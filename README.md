# AutoTyper

Paste text into AutoTyper, click into any window, press a hotkey, and AutoTyper types the text
there as real keystrokes. Because it types instead of pasting, it works in fields that block
pasting.

![AutoTyper window](docs/screenshot.png)

## Download

Get the latest `AutoTyper-<version>-win-x64.zip` from the [Releases page](../../releases/latest),
unzip it, and double-click `AutoTyper.exe`. There is nothing to install and you do not need .NET.

Windows may show "Windows protected your PC" the first time, because the program is not signed.
Click **More info**, then **Run anyway**.

## How to use it

1. Paste your text into the big box.
2. Choose how it types:
   - **Letters** types one character at a time.
   - **Words** types one word at a time.
3. Set the pace:
   - **Delay (ms)** is the wait after each letter or word, from 0 to 60,000 ms.
   - In Words mode you can tick **Words per minute** instead and set a speed from 1 to 1,000.
     The speed is a target: the time spent typing a word counts toward its share of the minute.
4. Click into the window you want to type into and press **Ctrl+Alt+T**. Press it again to stop.

To change the hotkey, click the Hotkey box and press the combination you want. The hotkey, mode,
delay and speed are remembered. The text you paste is never saved.

Emoji, accented letters and line breaks all work.

## Good to know

- Windows does not let a program type into a window that is running as administrator, unless that
  program is also run as administrator.
- Windows 11 Notepad has its own spell check and autocorrect, which can rewrite words as they are
  typed, especially misspelled ones. Turn them off in Notepad's settings if you need exact text
  there. Most other programs are not affected.
- How fast text can be typed depends on your PC. A delay of 0 means "as fast as Windows allows",
  which on some machines is only a few dozen characters a second.
- AutoTyper only types into the window that has focus when you press the hotkey. It will not type
  into its own window.

Tested on Windows 11 (64-bit).

## Build it yourself

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) on Windows.

```powershell
dotnet build
dotnet test                       # the hotkey tests need a normal desktop session
powershell -File scripts\package.ps1   # builds release\AutoTyper-<version>-win-x64.zip
```

`scripts\e2e.ps1 -ExePath <AutoTyper.exe>` runs the real app against a scratch text box and checks
what arrives. It takes over the keyboard while it runs, so leave the PC alone until it finishes, and
close any copy of AutoTyper first.

## Making a release

1. Change `<Version>` in `AutoTyper/AutoTyper.csproj` and commit.
2. Tag the commit with the same number and push the tag:

   ```powershell
   git tag v1.0.1
   git push origin v1.0.1
   ```

GitHub Actions then runs the tests, builds the zip and attaches it to a new release.

## License

MIT. See [LICENSE](LICENSE).
