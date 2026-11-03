# Testing AutoTyper on a Mac

A checklist for trying the macOS version of AutoTyper on a real Mac. So far the Mac code has been
built and unit-tested without a Mac to run it on, so this is its first real run. Work through it and
report what you see by opening an issue on GitHub (https://github.com/maxdaddy09/AutoTyper/issues).
Passes are good news, but anything that surprised you is worth a note even if it worked.

**You need:** an Apple Silicon Mac with macOS 15 or newer, TextEdit, a web browser (Safari, and Chrome
if you have it) and Terminal.

Tick each box as you go. "Expect" says what should happen. When it does not, write down what happened
instead and use the last section, "If something fails, report this:". The exact wording of macOS dialogs
differs a little between versions, so small differences in those are fine.

## 1. Download and unzip

- [ ] Download `AutoTyper-<version>-macos-arm64.zip` (from the Releases page, or from the build
  artifact if there is no release yet) and unzip it by double-clicking it in Finder.
  - A download from the GitHub Actions page (a build artifact) is a zip inside a zip: unzip what you
    downloaded first, and then unzip the `AutoTyper-<version>-macos-arm64.zip` that comes out of it.
  - Expect: a folder `AutoTyper-<version>-macos-arm64` holding `AutoTyper.app`, `README.txt` and
    `LICENSE.txt`.
- [ ] Optional: in Terminal, go to that folder (type `cd `, drag the folder into the Terminal window,
  press Return) and run `codesign --verify --deep --strict --verbose=2 AutoTyper.app`.
  - Expect: no error (it reports the app as valid on disk).

## 2. First launch through Gatekeeper

- [ ] Double-click `AutoTyper.app`.
  - Expect: macOS refuses to open it and says it cannot verify the app. Write down the wording and the
    buttons.
- [ ] Dismiss the warning (**Done**). Open **System Settings > Privacy & Security** and scroll down to
  the **Security** section.
  - Expect: a message about AutoTyper with an **Open Anyway** button.
- [ ] Click **Open Anyway**, authenticate, then open the app again and click **Open**.
  - Expect: AutoTyper's window opens.
- [ ] Optional, for the record: unzip the same download again somewhere else and Control-click that
  `AutoTyper.app` > **Open**. Does the dialog offer an **Open** button? (We believe macOS 15 no longer
  does. Say what you see.)
- [ ] Optional: unzip it once more and run `xattr -dr com.apple.quarantine /path/to/AutoTyper.app`
  (drag the app onto the Terminal window to fill in the path), then double-click it.
  - Expect: it opens without the warning.

## 3. The window, before Accessibility is allowed

For this section AutoTyper must not be allowed yet: it must be missing from, or switched off in,
**System Settings > Privacy & Security > Accessibility**. If you used an earlier build, select it in
that list and remove it with **-** first, then quit and reopen AutoTyper.

- [ ] The window shows: the title AutoTyper, a big text box, a Hotkey box with `Control+Option+T`,
  Mode (Letters and Words), Delay (ms) 50, a Words per minute tick box with a number, a status line,
  and a grey hint line at the bottom.
- [ ] The status line reads: `AutoTyper needs permission to type. Open Accessibility settings, switch
  AutoTyper on, then try again.`
- [ ] A button **Open Accessibility settings** is shown under the status line.
  - This button is hidden once the permission is granted, and so far only a unit test of the window's
    layout covers it, so this is its first real check.
- [ ] Click the button.
  - Expect: System Settings opens on the **Accessibility** page of Privacy & Security, not just on its
    front page. Note whether AutoTyper is already in the list.

## 4. Allow it

- [ ] Switch AutoTyper on in the Accessibility list. If it is not in the list, click **+** and choose
  `AutoTyper.app`, then switch it on. macOS may ask for your password.
  - Write down which of the two was needed. It tells us whether macOS adds the app to the list by
    itself.
- [ ] Go on to section 5 without restarting AutoTyper. The window checks the permission again each
  time the hotkey is pressed. If section 5 still reports the missing permission, quit and reopen
  AutoTyper, and say that you had to.

## 5. The hotkey works

This is the most important check. Put AutoTyper's window beside a TextEdit document so you can see
both.

- [ ] In TextEdit, open a new document (File > New). In AutoTyper, type or paste a few sentences into
  the big box and set Delay to 200 ms, so typing lasts long enough to watch. Click into the TextEdit
  document so it is in front.
- [ ] Press **Control+Option+T**.
  - Expect: AutoTyper's status line changes to `Typing…` (later with progress such as
    `Typing… 12/140`), the text appears in the TextEdit document, and at the end the status reads
    `Done`. The **Open Accessibility settings** button is gone.
  - If the status never changes, AutoTyper never received the press. That is the result I most need
    to know about: it means the global hotkey is not being delivered. Report it exactly as "no press
    received", with your macOS version; there is a known alternative approach to try if that is what
    happens.
  - If the status says `Typing…` but nothing appears in TextEdit, report that as well.
- [ ] With text in the box and AutoTyper's own window in front, press the hotkey.
  - Expect: the status reads `Click into the target window first`, and nothing is typed.
- [ ] Empty AutoTyper's text box, click into TextEdit and press the hotkey.
  - Expect: the status reads `Nothing to type`.

## 6. Changing the hotkey

- [ ] Click the Hotkey box and press **Control+Option+Y**.
  - Expect: the box shows `Control+Option+Y` and the status reads `Ready`.
- [ ] Put text in the box, click into TextEdit and press Control+Option+Y.
  - Expect: it types at once, with no restart. Control+Option+T now does nothing.
- [ ] Click the Hotkey box and press only the letter `T`.
  - Expect: the status says that hotkey can't be used, and the box keeps `Control+Option+Y`.
- [ ] Optional: try a combination macOS already uses, such as Command+Space (Spotlight). Write down what
  the box and the status show, and whether it fires.
- [ ] On any macOS version: set `Option+Shift+T` and press it with TextEdit in front. On macOS 15
  (reported on 15.0 and 15.1, possibly later), shortcuts whose only modifier is Option (or Option+Shift)
  may register without firing. Write down whether it types, and your macOS version.
- [ ] Click the Hotkey box and press **Control+Command+K**.
  - Expect: the box shows `Control+Command+K` and the status reads `Ready`. Put text in the box, click
    into TextEdit and press Control+Command+K: it types. This checks the Command key as a hotkey
    modifier.
- [ ] Set the hotkey back to `Control+Option+T` for the rest.

## 7. Typing into TextEdit

Use Delay 50 and start each check in an empty TextEdit document. Use Letters mode, except for the two
checks marked **Words mode**: they exercise the limit on how much text goes into one key event, which
Letters mode never reaches (the first of them explains why).

- [ ] Plain text: `The quick brown fox jumps over the lazy dog. 0123456789`
  - Expect: it arrives exactly as written.
- [ ] Three short lines with a blank line between two of them, and a line that starts with a Tab.
  - Expect: the line breaks and the tab arrive where they were.
- [ ] **Words mode.** A long string with no spaces:

  ```
  0123456789012345678901234567890123456789012345678901234567890123456789012345678901234567890123456789
  ```

  - AutoTyper hands macOS at most 20 characters per key event and splits longer text into pieces. That
    limit was found by experiment, not from Apple's documentation, so check that it holds.
  - Why Words mode: in Letters mode every character is sent on its own, so no key event ever comes near
    20 characters and this check would pass without testing anything. In Words mode this string is one
    word, which AutoTyper sends as five pieces of 20 characters (20, 20, 20, 20, 20).
  - Expect: all 100 digits arrive, in order, none dropped or repeated.
  - Then switch back to Letters.
- [ ] Accents, other scripts and emoji:

  ```
  café naïve Zoë Ångström ñ ü ß 日本語 😀 👍 🎉
  ```

  - Expect: it arrives exactly. No question marks, boxes or missing characters.
- [ ] **Words mode.** Emoji at a piece boundary (an emoji counts as two of those characters, and the
  first one sits across the boundary of the 20-character pieces):

  ```
  aaaaaaaaaaaaaaaaaaa😀bbbbbbbbbbbbbbbbbbb😀ccc
  ```

  - Like the digits, this line has no spaces, so Words mode sends it as one word that is split into
    pieces. In Letters mode it would never be split. AutoTyper should end the first piece one character
    early (after the 19 letters) rather than cut the emoji in half.
  - Expect: it arrives exactly, with both emoji whole.
  - Then switch back to Letters.
- [ ] TextEdit can rewrite what is typed. With Edit > Spelling and Grammar > **Correct Spelling
  Automatically** and Edit > Substitutions > **Smart Quotes** on, type `teh "quoted" don't ` and see
  whether TextEdit changes it. That is TextEdit, not AutoTyper. Turn both off and type it again.
  - Expect: with both off it arrives exactly, with straight quotes.

## 8. Typing into a browser

- [ ] In Safari, click into any text box that you do not mind typing in, such as a search box (do not
  press Return), and type `Hello from AutoTyper`.
  - Expect: it arrives exactly. Repeat in Chrome if you have it.
- [ ] A long word in each browser. Browsers are where multi-character key events are most likely to be
  handled differently, so this repeats the 20-character check from section 7 there. Choose **Words**
  mode, click into a text box in Safari, and type the 100-digit string from section 7 (one word, sent as
  five pieces of 20 characters). Repeat in Chrome if you have it, then switch back to Letters.
  - Expect: all 100 digits arrive, in order, none dropped or repeated, in each browser.
- [ ] A page that blocks pasting. Save this as `nopaste.html` (in TextEdit choose Format > Make Plain Text
  first, because TextEdit saves rich text by default) and open it in the browser:

  ```html
  <textarea rows="8" cols="60" onpaste="return false" placeholder="Paste is blocked here"></textarea>
  ```

  First copy some text and press Command+V in the box.
  - Expect: nothing is pasted. Now type into the same box with AutoTyper: the text arrives.
- [ ] Type the accents and emoji line from section 7 into a browser box.
  - Expect: it arrives exactly.

## 9. Words mode

- [ ] Choose Words, set Delay to 300 ms and type `one two three four five six`.
  - Expect: the words arrive one at a time about 0.3 seconds apart, with the spaces, and the final text
    is exact.
- [ ] Tick **Words per minute** and set 120, then type a text of about 20 words.
  - Expect: the Delay box is greyed out. About two words arrive per second, so 20 words take about
    ten seconds. Try 30 as well: about one word every two seconds.
- [ ] Switch back to Letters.
  - Expect: the Words per minute tick box is greyed out and the Delay box is usable again.

## 10. Stopping mid-run

- [ ] Choose Letters, Delay 100 ms, put a long text in the box, click into TextEdit and press the
  hotkey. After a few seconds press it again.
  - Expect: typing stops at once and the status reads `Stopped`. Wait five seconds: nothing more
    arrives.
- [ ] Press the hotkey once more.
  - Expect: a new run starts from the beginning of the text.
- [ ] A very large paste. In Terminal run this, which puts about 100,000 characters on the clipboard:

  ```sh
  yes 'lorem ipsum dolor sit amet' | head -n 4000 | pbcopy
  ```

  Choose Letters with Delay 0, empty the text box, and paste with Command+V.
  - Expect: AutoTyper's window stays responsive (you can scroll and click in the text box, and the
    window moves and closes normally).
  - Click into TextEdit and press the hotkey: typing starts. Press the hotkey again after a few
    seconds: typing stops and the status reads `Stopped`.

## 11. Holding the hotkey's modifiers when typing starts

Pressing the hotkey leaves Control and Option held down. AutoTyper waits until you let go of them (up
to about three seconds) before it types, because typing with them down would turn letters into
shortcuts. Whether that is enough on a real Mac is not known, so check both cases.

- [ ] Use Letters, Delay 10 and the text `hello world, this is a test`. Click into TextEdit. Press
  Control+Option+T and keep Control and Option held for about one second after pressing T, then
  release them.
  - Expect: typing starts when you let go, the text is exact, and there is no beep, no menu action
    and no special character (Option turns letters into symbols).
- [ ] Again, but keep Control and Option held for five seconds.
  - By design AutoTyper gives up waiting after about three seconds and types while the keys are still
    down. Write down whether the text is still intact or whether shortcuts fired (beeps, odd
    characters, the cursor jumping around).

## 12. Another app holding the same key combination

- [ ] In Terminal, run `open -n /path/to/AutoTyper.app`. The `-n` starts a second copy, so two copies
  try to register `Control+Option+T`. (Any other program with a global shortcut will do instead:
  give AutoTyper the same shortcut.) Then press the combination with TextEdit in front.
  - Expect, and either is fine because I need to learn which: the second copy shows `Hotkey
    Control+Option+T is in use` with its Hotkey box reading `(none)`; or both register. Write down
    which happened, and which copy types.
- [ ] Quit the second copy. Both copies share one settings file, so do not change settings in it.

## 13. The hotkey while an AutoTyper menu is open

- [ ] Put a short text with no line break in the box. Click AutoTyper in the menu bar (its application
  menu) and keep the menu open. Press the hotkey.
  - Expect: AutoTyper's status line changes to `Click into the target window first`, so the press
    arrived even though a menu was open. If it only changes after you close the menu, write that down.
    Close the menu with Escape.
- [ ] The same with the right-click menu of the text box open.

## 14. Closing, reopening and settings

- [ ] Choose Words, set Delay to 123, tick **Words per minute** and set it to 90, change the hotkey to
  Control+Option+Y, and type something in the text box.
- [ ] Quit AutoTyper (close the window, or Command+Q) and open it again.
  - Expect: Words, Delay 123, Words per minute ticked at 90, Hotkey `Control+Option+Y`, status
    `Ready`, and an empty text box (the text is never saved).
  - Does closing the window quit the app? We expect so. If AutoTyper keeps running without a window,
    does the hotkey still do anything? Say so.
- [ ] In Terminal run `cat "$HOME/Library/Application Support/AutoTyper/settings.json"`.
  - Expect: a small JSON file with Hotkey, Mode, DelayMs, UseWpm and Wpm, and none of your text.
- [ ] Nothing keeps typing after the window closes. Choose Letters, Delay 200, put a long text in the
  box, click into TextEdit and press the hotkey. While it types, click the red close button of
  AutoTyper's window.
  - Expect: typing stops. (A few characters may land in AutoTyper's own box between your click and
    the window closing. That is fine.) Wait five seconds: nothing more appears in TextEdit.
- [ ] Open AutoTyper again and press its hotkey.
  - Expect: it works, so the old hotkey was released properly.

## If something fails, report this:

- Your macOS version (Apple menu > About This Mac) and the chip (for example M2).
- What you did, step by step, what you expected, and what happened.
- The exact words in AutoTyper's status line at that moment (a screenshot is fine).
- For hotkey trouble: whether the Hotkey box shows a combination or `(none)`, and whether the status
  line changed at all when you pressed it.
- Whether the self-test passes. In Terminal go to the folder that holds `AutoTyper.app` (type `cd `,
  drag the folder into the Terminal window, press Return) and run:

  ```sh
  AutoTyper.app/Contents/MacOS/AutoTyper --selftest
  ```

  It should print `selftest ok`. If it prints lines starting `selftest failed:`, copy all of them. If
  macOS blocks the command in the same way as in section 2, allow the app first.
- If the app does not open, or the hotkey shows `(none)`, run `AutoTyper.app/Contents/MacOS/AutoTyper`
  from Terminal in the same way and copy what it prints. (Launching from Terminal makes Terminal the app
  that needs the Accessibility permission, so use this only for diagnosis.)
