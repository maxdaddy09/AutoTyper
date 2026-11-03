namespace AutoTyper.Tests;

public class TypingEngineTests
{
    sealed class FakeKeySender : IKeySender
    {
        public List<string> Calls { get; } = new();
        public int? FailOnCall { get; set; }

        bool Record(string call)
        {
            Calls.Add(call);
            return Calls.Count != FailOnCall;
        }

        public bool SendText(string text) => Record($"T:{text}");
        public bool SendEnter() => Record("Enter");
        public bool SendTab() => Record("Tab");
    }

    sealed class RecordingDelay
    {
        public List<int> Calls { get; } = new();
        public Action? OnCall { get; set; }

        public Task Delay(int milliseconds, CancellationToken ct)
        {
            Calls.Add(milliseconds);
            OnCall?.Invoke();
            return ct.IsCancellationRequested ? Task.FromCanceled(ct) : Task.CompletedTask;
        }
    }

    static Task<TypingResult> Run(
        FakeKeySender sender,
        string text,
        TypingMode mode = TypingMode.Letters,
        int delayMs = 0,
        RecordingDelay? delay = null,
        CancellationToken ct = default)
    {
        var engine = new TypingEngine(sender, (delay ?? new RecordingDelay()).Delay);
        return engine.RunAsync(text, mode, delayMs, ct);
    }

    [Fact]
    public async Task Letters_SendsEachCharacterInOrder()
    {
        var sender = new FakeKeySender();

        var result = await Run(sender, "ab");

        Assert.Equal(TypingResult.Completed, result);
        Assert.Equal(new[] { "T:a", "T:b" }, sender.Calls);
    }

    [Fact]
    public async Task Words_SendsEachWordWithItsTrailingSpace()
    {
        var sender = new FakeKeySender();

        await Run(sender, "hi you", TypingMode.Words);

        Assert.Equal(new[] { "T:hi ", "T:you" }, sender.Calls);
    }

    [Fact]
    public async Task Letters_NewlineAndTab_UseEnterAndTab()
    {
        var sender = new FakeKeySender();

        await Run(sender, "a\nb\tc");

        Assert.Equal(new[] { "T:a", "Enter", "T:b", "Tab", "T:c" }, sender.Calls);
    }

    [Fact]
    public async Task Words_NewlineInsideUnit_SplitsIntoTextAndEnter()
    {
        var sender = new FakeKeySender();

        await Run(sender, "hi\nyou", TypingMode.Words);

        Assert.Equal(new[] { "T:hi", "Enter", "T:you" }, sender.Calls);
    }

    [Theory]
    [InlineData(TypingMode.Letters)]
    [InlineData(TypingMode.Words)]
    public async Task CrLf_SendsExactlyOneEnter(TypingMode mode)
    {
        var sender = new FakeKeySender();

        await Run(sender, "a\r\nb", mode);

        Assert.Single(sender.Calls, call => call == "Enter");
    }

    [Fact]
    public async Task ControlCharacters_AreDropped_WithoutDelay()
    {
        var sender = new FakeKeySender();
        var delay = new RecordingDelay();

        await Run(sender, "a\u0000\u001b\fb", delayMs: 10, delay: delay);

        Assert.Equal(new[] { "T:a", "T:b" }, sender.Calls);
        Assert.Equal(new[] { 10 }, delay.Calls);
    }

    [Fact]
    public async Task LoneSurrogate_DoesNotThrow()
    {
        var sender = new FakeKeySender();

        var result = await Run(sender, "\uD83Dx");

        Assert.Equal(TypingResult.Completed, result);
        Assert.Equal(new[] { "T:\uD83D", "T:x" }, sender.Calls);
    }

    [Fact]
    public async Task Delay_IsAppliedBetweenUnitsButNotAfterTheLast()
    {
        var delay = new RecordingDelay();

        await Run(new FakeKeySender(), "abc", delayMs: 25, delay: delay);

        Assert.Equal(new[] { 25, 25 }, delay.Calls);
    }

    [Fact]
    public async Task ZeroDelay_NeverCallsTheDelayFunction()
    {
        var delay = new RecordingDelay();

        await Run(new FakeKeySender(), "abc", delayMs: 0, delay: delay);

        Assert.Empty(delay.Calls);
    }

    [Fact]
    public async Task PreCancelledToken_SendsNothing_AndReturnsCancelled()
    {
        var sender = new FakeKeySender();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await Run(sender, "abc", ct: cts.Token);

        Assert.Equal(TypingResult.Cancelled, result);
        Assert.Empty(sender.Calls);
    }

    [Fact]
    public async Task CancelDuringDelay_StopsFurtherSends()
    {
        var sender = new FakeKeySender();
        using var cts = new CancellationTokenSource();
        var delay = new RecordingDelay { OnCall = cts.Cancel };

        var result = await Run(sender, "abc", delayMs: 10, delay: delay, ct: cts.Token);

        Assert.Equal(TypingResult.Cancelled, result);
        Assert.Equal(new[] { "T:a" }, sender.Calls);
    }

    [Fact]
    public async Task SenderFailure_StopsAndReturnsSendFailed()
    {
        var sender = new FakeKeySender { FailOnCall = 2 };

        var result = await Run(sender, "abc");

        Assert.Equal(TypingResult.SendFailed, result);
        Assert.Equal(new[] { "T:a", "T:b" }, sender.Calls);
    }

    [Fact]
    public async Task Progress_ReportsUnitsSentOfTotal()
    {
        var engine = new TypingEngine(new FakeKeySender(), new RecordingDelay().Delay);
        var reports = new List<(int Sent, int Total)>();
        engine.Progress += (sent, total) => reports.Add((sent, total));

        await engine.RunAsync("ab", TypingMode.Letters, 0, CancellationToken.None);

        Assert.Equal(new[] { (1, 2), (2, 2) }, reports);
    }

    [Fact]
    public async Task EmptyText_CompletesWithoutSending()
    {
        var sender = new FakeKeySender();

        var result = await Run(sender, "");

        Assert.Equal(TypingResult.Completed, result);
        Assert.Empty(sender.Calls);
    }
}
