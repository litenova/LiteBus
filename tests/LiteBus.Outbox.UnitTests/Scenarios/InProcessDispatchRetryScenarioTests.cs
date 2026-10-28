using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Dispatch.InProcess;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.Scenarios;

/// <summary>
///     Verifies in-process dispatched events retry through handler failures to published, dead letter when retries exhaust, and replay through the outbox manager.
/// </summary>
[Collection("Sequential")]
public sealed class InProcessDispatchRetryScenarioTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies a handler that fails twice then succeeds ends published after retries.
    /// </summary>
    [Fact]
    public async Task Handler_that_fails_twice_then_succeeds_should_end_published()
    {
        var state = new ReactionState(failuresBeforeSuccess: 2);
        var provider = BuildProvider(state, maxAttempts: 5);
        var outbox = provider.GetRequiredService<IOutbox>();

        var receipt = await outbox.EnqueueAsync(
            OutboxEnqueueItem<SettlementEvent>.From(new SettlementEvent { SettlementId = Guid.NewGuid() })).ConfigureAwait(false);

        await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        var envelope = provider.GetRequiredService<InMemoryOutboxStore>().Get(receipt.Id);
        envelope.Status.Should().Be(OutboxStatus.Published);
        envelope.AttemptCount.Should().Be(3);
        state.Attempts.Should().Be(3);
        state.HandledCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies a handler that always fails ends dead lettered, and manager requeue puts it back to dispatch.
    /// </summary>
    [Fact]
    public async Task Handler_that_always_fails_should_dead_letter_and_requeue_to_dispatch()
    {
        var state = new ReactionState(failuresBeforeSuccess: int.MaxValue);
        var provider = BuildProvider(state, maxAttempts: 3);
        var outbox = provider.GetRequiredService<IOutbox>();

        var receipt = await outbox.EnqueueAsync(
            OutboxEnqueueItem<SettlementEvent>.From(new SettlementEvent { SettlementId = Guid.NewGuid() })).ConfigureAwait(false);

        await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        store.Get(receipt.Id).Status.Should().Be(OutboxStatus.DeadLettered);
        state.HandledCount.Should().Be(0);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var requeued = await manager.RequeueAsync([receipt.Id]).ConfigureAwait(false);
        requeued.Should().Be(new RequeueResult(1, 1));
        store.Get(receipt.Id).Status.Should().Be(OutboxStatus.Pending);

        state.FailuresBeforeSuccess = 0;
        await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        var replayed = store.Get(receipt.Id);
        replayed.Status.Should().Be(OutboxStatus.Published);
        replayed.AttemptCount.Should().Be(1);
        state.HandledCount.Should().Be(1);
    }

    private static ServiceProvider BuildProvider(ReactionState state, int maxAttempts)
    {
        return new ServiceCollection()
            .AddSingleton(state)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder => builder.Register<SettlementReaction>());

                registry.AddOutbox(builder =>
                {
                    builder.Contracts.Register<SettlementEvent>("orders.events.settlement");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "scenario-test",
                        Retry = new RetryOptions
                        {
                            MaxAttempts = maxAttempts,
                            InitialDelay = TimeSpan.Zero,
                            UseJitter = false
                        }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();
    }

    private sealed record SettlementEvent
    {
        public Guid SettlementId { get; init; }
    }

    /// <summary>
    ///     Fails a configured number of attempts before handling.
    /// </summary>
    private sealed class ReactionState
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="ReactionState" /> class.
        /// </summary>
        /// <param name="failuresBeforeSuccess">The number of attempts that throw before handling succeeds.</param>
        public ReactionState(int failuresBeforeSuccess)
        {
            FailuresBeforeSuccess = failuresBeforeSuccess;
        }

        /// <summary>
        ///     Gets the number of dispatch attempts observed.
        /// </summary>
        public int Attempts { get; private set; }

        /// <summary>
        ///     Gets the number of successful handles.
        /// </summary>
        public int HandledCount { get; private set; }

        /// <summary>
        ///     Gets or sets the number of attempts that throw before handling succeeds.
        /// </summary>
        public int FailuresBeforeSuccess { get; set; }

        /// <summary>
        ///     Records one attempt, throwing while the failure budget remains.
        /// </summary>
        public void Attempt()
        {
            Attempts++;

            if (Attempts <= FailuresBeforeSuccess)
            {
                throw new InvalidOperationException("Settlement reaction failure.");
            }

            HandledCount++;
        }
    }

    /// <summary>
    ///     The settlement reaction under test.
    /// </summary>
    private sealed class SettlementReaction : IEventHandler<SettlementEvent>
    {
        private readonly ReactionState _state;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SettlementReaction" /> class.
        /// </summary>
        /// <param name="state">The state that fails attempts before handling.</param>
        public SettlementReaction(ReactionState state)
        {
            _state = state;
        }

        /// <inheritdoc />
        public Task HandleAsync(SettlementEvent message, CancellationToken cancellationToken = default)
        {
            _state.Attempt();
            return Task.CompletedTask;
        }
    }
}
