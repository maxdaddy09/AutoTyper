using System.Diagnostics;
using System.Text;

namespace AutoTyper;

public enum TypingResult { Completed, Cancelled, SendFailed }

public sealed class TypingEngine
{
    enum KeyKind { Text, Enter, Tab }

    readonly record struct KeyAction(KeyKind Kind, string Text);

    // IsWord is false for a unit made only of whitespace, which has no word to pace.
    readonly record struct Unit(bool IsWord, List<KeyAction> Actions);

    readonly IKeySender sender;
    readonly Func<int, CancellationToken, Task> delay;
    readonly Func<double> elapsedMs;

    public TypingEngine(
        IKeySender sender,
        Func<int, CancellationToken, Task>? delay = null,
        Func<double>? elapsedMs = null)
    {
        this.sender = sender;
        this.delay = delay ?? Task.Delay;
        this.elapsedMs = elapsedMs ?? (() => Stopwatch.GetTimestamp() * 1000.0 / Stopwatch.Frequency);
    }

    // (unitsSent, totalUnits), raised on whichever thread runs the job.
    public event Action<int, int>? Progress;

    public Task<TypingResult> RunAsync(string text, TypingMode mode, int delayMs, CancellationToken ct) =>
        RunAsync(text, mode, TypingPace.AfterEach(delayMs), ct);

    public async Task<TypingResult> RunAsync(string text, TypingMode mode, TypingPace pace, CancellationToken ct)
    {
        // Units that turn into no key presses (control characters) are dropped here,
        // so they never cause a delay.
        var units = TextChunker.Chunk(text, mode)
            .Select(chunk => new Unit(chunk.Any(c => !char.IsWhiteSpace(c)), Plan(chunk)))
            .Where(unit => unit.Actions.Count > 0)
            .ToList();

        // Without this, Windows rounds short waits up to about 15 ms.
        WindowsTimerResolution.Begin();
        try
        {
            var nextStart = 0.0;
            var wordsStarted = false;
            for (var i = 0; i < units.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                if (pace.Scheduled && units[i].IsWord)
                {
                    var now = elapsedMs();
                    if (!wordsStarted)
                    {
                        nextStart = now;
                        wordsStarted = true;
                    }
                    else
                    {
                        var waitMs = (int)Math.Round(nextStart - now, MidpointRounding.AwayFromZero);

                        // On time: wait out the rest of the slot and keep the schedule, so rounding
                        // never adds up. Already late: restart the schedule from now, so a slow
                        // word is not followed by a burst of fast ones.
                        if (waitMs > 0) await delay(waitMs, ct);
                        else nextStart = now;
                    }

                    nextStart += pace.Milliseconds;
                }

                foreach (var action in units[i].Actions)
                {
                    if (!Send(action)) return TypingResult.SendFailed;
                }

                Progress?.Invoke(i + 1, units.Count);
                if (!pace.Scheduled && pace.Milliseconds > 0 && i < units.Count - 1)
                    await delay((int)pace.Milliseconds, ct);
            }

            return TypingResult.Completed;
        }
        catch (OperationCanceledException)
        {
            return TypingResult.Cancelled;
        }
        finally
        {
            WindowsTimerResolution.End();
        }
    }

    bool Send(KeyAction action) => action.Kind switch
    {
        KeyKind.Enter => sender.SendEnter(),
        KeyKind.Tab => sender.SendTab(),
        _ => sender.SendText(action.Text),
    };

    static List<KeyAction> Plan(string unit)
    {
        var actions = new List<KeyAction>();
        var run = new StringBuilder();

        void FlushRun()
        {
            if (run.Length == 0) return;
            actions.Add(new KeyAction(KeyKind.Text, run.ToString()));
            run.Clear();
        }

        foreach (var c in unit)
        {
            if (c == '\n')
            {
                FlushRun();
                actions.Add(new KeyAction(KeyKind.Enter, ""));
            }
            else if (c == '\t')
            {
                FlushRun();
                actions.Add(new KeyAction(KeyKind.Tab, ""));
            }
            else if (!char.IsControl(c))
            {
                run.Append(c);
            }
        }

        FlushRun();
        return actions;
    }
}
