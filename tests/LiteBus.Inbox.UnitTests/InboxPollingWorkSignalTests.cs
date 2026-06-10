using LiteBus.Inbox;

namespace LiteBus.Inbox.UnitTests;

public sealed class InboxPollingWorkSignalTests
{
    [Fact]
    public async Task WaitForWorkOrDelayAsync_zero_interval_completes_immediately()
    {
        var signal = new InboxPollingWorkSignal();

        await signal.WaitForWorkOrDelayAsync(TimeSpan.Zero);
    }

    [Fact]
    public async Task WaitForWorkOrDelayAsync_positive_interval_waits()
    {
        var signal = new InboxPollingWorkSignal();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await signal.WaitForWorkOrDelayAsync(TimeSpan.FromMilliseconds(50));

        stopwatch.Elapsed.Should().BeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(40));
    }
}
