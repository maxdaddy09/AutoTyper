AutoTyper for Mac
=================

Paste text into AutoTyper, click into any window, press a hotkey, and AutoTyper types
the text there as real keystrokes. Because it types instead of pasting, it should work
in fields that block pasting.

Needs macOS 15 (Sequoia) or newer. The Intel version has not been tested yet. The
Apple Silicon version is the one being tested. The Mac version is new, so if something
does not work for you, please say so on the project's GitHub page.

Open it the first time
----------------------
AutoTyper is not signed by a registered Apple developer, so macOS blocks it at first.
You only have to allow it once for each version.

1. Unzip the download, and move AutoTyper.app wherever you like (Applications is fine).
2. Double-click AutoTyper.app. macOS says it cannot open or verify it. Close that
   message (Done or OK). Do not move it to the Trash.
3. Open System Settings -> Privacy & Security and scroll down to the Security section.
   Next to the message about AutoTyper, click "Open Anyway", then enter your password.
4. Double-click AutoTyper.app again and click Open.

If the "Open Anyway" button is not there, try to open the app again first; macOS only
offers it for a while after a blocked attempt.

If you would rather use Terminal, you can skip steps 2 and 3: type
"xattr -dr com.apple.quarantine " (with a space at the end), drag AutoTyper.app onto the
Terminal window, press Return, and then open the app as usual.

If macOS says AutoTyper is damaged after you unzipped it with a third-party tool, unzip
the download again with Finder's Archive Utility (double-click the zip) or, in Terminal,
with "ditto -x -k <zip> <folder>", and use the xattr command above if it still will not
open.

The exact words on these screens can differ a little between macOS versions.

Let it type (Accessibility permission)
--------------------------------------
macOS only lets an app type for you once you allow it.

1. Open System Settings -> Privacy & Security -> Accessibility. (AutoTyper has a
   button, "Open Accessibility settings", that takes you there.)
2. Switch AutoTyper on. If it is not in the list, click the + button, choose
   AutoTyper.app, and switch it on.
3. If AutoTyper still says it needs permission, quit it and open it again.

After you install a newer version, macOS may stop trusting it, because every build has
its own signature. If AutoTyper stops typing after an update, switch it off and on again
in the Accessibility list, or remove it with the - button and add it again.

How to use
----------
1. Paste your text into the big box.
2. Pick how it types:
     Letters - one character at a time.
     Words   - one word at a time.
   Then set the delay in milliseconds, or, in Words mode, tick "Words per minute"
   and set a speed instead.
3. Click into the window you want to type into, then press Control+Option+T.
   Press it again to stop.

To change the hotkey, click the Hotkey box and press the combination you want. It needs
Control, Option, Shift or Command, or can be an F-key on its own. Keys are named by their
position on a US keyboard, so on other layouts (for example Dvorak or AZERTY) the name
shown can differ from the letter printed on the key cap, and the default
Control+Option+T is the key in that position. The hotkey, mode, delay and speed are
remembered. The text you paste is never saved.

Good to know
------------
- On macOS 15 (reported on 15.0 and 15.1, possibly later), shortcuts whose only
  modifier is Option (or Option+Shift) may be accepted but never fire. The default,
  Control+Option+T, is not affected. When you pick your own, add Control or Command as
  well.
- TextEdit can rewrite what is typed: turn off "Correct spelling automatically" and
  smart quotes (Edit -> Spelling and Grammar, and Edit -> Substitutions) if you want
  exact text there. Most other programs are not affected.
- AutoTyper only types into the window that has focus when you press the hotkey. It will
  not type into its own window.
- How fast text can be typed also depends on your Mac and on the program you type into.
