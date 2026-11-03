namespace AutoTyper.Tests;

// Timing is checked with a fake clock: sending a word costs a chosen number of milliseconds and
// every wait advances the clock by exactly the time asked for.
public class TypingPaceTests
{
    sealed class FakeClock
    {
        public double Now { get; set; }
    }

    sealed class TimedSender : IKeySender
    {
        readonly FakeClock clock;
        readonly Queue<double> costs;

        public TimedSender(FakeClock clock, params double[] costs)
        {
            this.clock = clock;
            this.costs = new Queue<double>(costs);
        }

        public List<string> Calls { get; } = new();

        bool Record(string call)
        {
            Calls.Add(call);
            if (costs.Count > 0) clock.Now += costs.Dequeue();
            return true;
        }

        public bool SendText(string text) => Record($"T:{text}");
        public bool SendEnter() => Record("Enter");
        public bool SendTab() => Record("Tab");
    }

    sealed class TimedDelay
    {
        readonly FakeClock clock;

        public TimedDelay(FakeClock clock) => this.clock = clock;

        public List<int> Calls { get; } = new();
        public Action? OnCall { get; set; }

        public Task Delay(int milliseconds, CancellationToken ct)
        {
            Calls.Add(milliseconds);
            clock.Now += milliseconds;
            OnCall?.Invoke();
            return ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;
        }
    }

    static TypingEngine Engine(FakeClock clock, TimedSender sender, TimedDelay delay) =>
        new(sender, delay.Delay, () => clock.Now);

    [Fact]
    public async Task WordsPerMinute_WaitsOneIntervalBetweenWords()
    {
        var clock = new FakeClock();
        var sender = new TimedSender(clock);
        var delay = new TimedDelay(clock);

        var result = await Engine(clock, sender, delay).RunAsync(
            "one two three", TypingMode.Words, TypingPace.WordsPerMinute(120), CancellationToken.None);

        Assert.Equal(TypingResult.Completed, result);
        Assert.Equal(new[] { "T:one ", "T:two ", "T:three" }, sender.Calls);
        Assert.Equal(new[] { 500, 500 }, delay.Calls);
    }

    [Fact]
    public async Task WordsPerMinute_TimeSpentTypingCountsTowardTheInterval()
    {
        var clock = new FakeClock();
        var sender = new TimedSender(clock, 100, 100, 100);
        var delay = new TimedDelay(clock);

        await Engine(clock, sender, delay).RunAsync(
            "one two three", TypingMode.Words, TypingPace.WordsPerMinute(120), CancellationToken.None);

        Assert.Equal(new[] { 400, 400 }, delay.Calls);
    }

    [Fact]
    public async Task WordsPerMinute_SlowWordIsNotFollowedByACatchUpBurst()
    {
        var clock = new FakeClock();
        var sender = new TimedSender(clock, 2500);
        var delay = new TimedDelay(clock);

        await Engine(clock, sender, delay).RunAsync(
            "a b c", TypingMode.Words, TypingPace.WordsPerMinute(60), CancellationToken.None);

        // The first word overran two intervals. The second follows at once, and the third waits
        // a full interval after it instead of the shorter gap the original schedule would give.
        Assert.Equal(new[] { "T:a ", "T:b ", "T:c" }, sender.Calls);
        Assert.Equal(new[] { 1000 }, delay.Calls);
    }

    [Fact]
    public async Task WordsPerMinute_SingleWordNeverWaits()
    {
        var clock = new FakeClock();
        var delay = new TimedDelay(clock);

        await Engine(clock, new TimedSender(clock), delay).RunAsync(
            "hello", TypingMode.Words, TypingPace.WordsPerMinute(60), CancellationToken.None);

        Assert.Empty(delay.Calls);
    }

    [Fact]
    public async Task WordsPerMinute_LeadingWhitespaceDoesNotUseAWordSlot()
    {
        var clock = new FakeClock();
        var sender = new TimedSender(clock);
        var delay = new TimedDelay(clock);

        await Engine(clock, sender, delay).RunAsync(
            "  hi there", TypingMode.Words, TypingPace.WordsPerMinute(60), CancellationToken.None);

        Assert.Equal(new[] { "T:  ", "T:hi ", "T:there" }, sender.Calls);
        Assert.Equal(new[] { 1000 }, delay.Calls);
    }

    [Fact]
    public async Task WordsPerMinute_NewlineInsideAWordIsStillOneSlot()
    {
        var clock = new FakeClock();
        var sender = new TimedSender(clock);
        var delay = new TimedDelay(clock);

        await Engine(clock, sender, delay).RunAsync(
            "hi\nyou ok", TypingMode.Words, TypingPace.WordsPerMinute(60), CancellationToken.None);

        Assert.Equal(new[] { "T:hi", "Enter", "T:you ", "T:ok" }, sender.Calls);
        Assert.Equal(new[] { 1000, 1000 }, delay.Calls);
    }

    [Fact]
    public async Task WordsPerMinute_FractionalIntervalDoesNotDrift()
    {
        var clock = new FakeClock();
        var delay = new TimedDelay(clock);

        // 70 wpm is one word every 857.14... ms: seven waits must add up to 6000 ms, not the
        // 6006 ms that rounding every wait on its own would give.
        await Engine(clock, new TimedSender(clock), delay).RunAsync(
            "a b c d e f g h", TypingMode.Words, TypingPace.WordsPerMinute(70), CancellationToken.None);

        Assert.Equal(7, delay.Calls.Count);
        Assert.InRange(delay.Calls.Sum(), 5999, 6001);
    }

    [Fact]
    public async Task WordsPerMinute_CancelDuringAWait_StopsFurtherWords()
    {
        var clock = new FakeClock();
        var sender = new TimedSender(clock);
        using var cts = new CancellationTokenSource();
        var delay = new TimedDelay(clock) { OnCall = cts.Cancel };

        var result = await Engine(clock, sender, delay).RunAsync(
            "one two three", TypingMode.Words, TypingPace.WordsPerMinute(120), cts.Token);

        Assert.Equal(TypingResult.Cancelled, result);
        Assert.Equal(new[] { "T:one " }, sender.Calls);
    }

    [Fact]
    public async Task AfterEach_WaitsTheSameAsTheDelayOverload()
    {
        var clock = new FakeClock();
        var delay = new TimedDelay(clock);

        await Engine(clock, new TimedSender(clock), delay).RunAsync(
            "abc", TypingMode.Letters, TypingPace.AfterEach(25), CancellationToken.None);

        Assert.Equal(new[] { 25, 25 }, delay.Calls);
    }

    [Theory]
    [InlineData(120, 500.0)]
    [InlineData(60, 1000.0)]
    [InlineData(1000, 60.0)]
    public void WordsPerMinute_IsSixtyThousandOverTheRate(int wpm, double expectedMs)
    {
        Assert.Equal(expectedMs, TypingPace.WordsPerMinute(wpm).Milliseconds, precision: 6);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void WordsPerMinute_BelowOneIsTreatedAsOne(int wpm)
    {
        Assert.Equal(60000.0, TypingPace.WordsPerMinute(wpm).Milliseconds);
    }
}
