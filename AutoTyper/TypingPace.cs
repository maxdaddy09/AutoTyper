namespace AutoTyper;

// How the engine spaces out text: wait a fixed time after each unit, or start one unit every
// so often (Scheduled), so the time spent typing a unit counts toward its slot.
public readonly record struct TypingPace(double Milliseconds, bool Scheduled)
{
    public static TypingPace AfterEach(int delayMs) => new(Math.Max(0, delayMs), false);

    public static TypingPace WordsPerMinute(int wpm) => new(60000.0 / Math.Max(1, wpm), true);
}
