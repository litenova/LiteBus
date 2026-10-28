using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;

namespace LiteBus.Outbox.UnitTests.ReleaseFixes;

/// <summary>
///     Verifies direct processor construction validates the retry ladder instead of failing mid-pass.
/// </summary>
public sealed class R3F13RetryLadderValidationTests
{
    /// <summary>
    ///     Verifies Schedule backoff with an empty ladder throws at construction.
    /// </summary>
    [Fact]
    public void Processor_with_schedule_backoff_and_empty_delays_should_throw_at_construction()
    {
        var store = new InMemoryOutboxStore();
        var options = new OutboxProcessorOptions
        {
            Retry = new RetryOptions { Backoff = RetryBackoff.Schedule, Delays = [] }
        };

        var act = () => new PipelinedOutboxProcessor(
            store,
            store,
            new RecordingDispatcher(),
            options,
            TimeProvider.System,
            []);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Delays is empty*");
    }

    /// <summary>
    ///     Verifies Schedule backoff with a negative ladder entry throws at construction.
    /// </summary>
    [Fact]
    public void Processor_with_negative_ladder_entry_should_throw_at_construction()
    {
        var store = new InMemoryOutboxStore();
        var options = new OutboxProcessorOptions
        {
            Retry = new RetryOptions { Backoff = RetryBackoff.Schedule, Delays = [TimeSpan.FromSeconds(-1)] }
        };

        var act = () => new PipelinedOutboxProcessor(
            store,
            store,
            new RecordingDispatcher(),
            options,
            TimeProvider.System,
            []);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class RecordingDispatcher : IOutboxDispatcher
    {
        public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
