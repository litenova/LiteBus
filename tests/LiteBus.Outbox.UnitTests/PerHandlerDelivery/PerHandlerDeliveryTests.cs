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

namespace LiteBus.Outbox.UnitTests.PerHandlerDelivery;

/// <summary>
///     Verifies per-handler outbox delivery writes one envelope per subscribed main handler and dispatches each envelope to its recorded handler alone.
/// </summary>
[Collection("Sequential")]
public sealed class PerHandlerDeliveryTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies one enqueue writes one envelope per subscribed main handler, each recording its handler full name with a distinct identifier.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_should_write_one_envelope_per_subscribed_handler()
    {
        var provider = BuildProvider();
        var outbox = provider.GetRequiredService<IOutbox>();

        var receipt = await outbox.EnqueueAsync(
            OutboxEnqueueItem<OrderEvent>.From(new OrderEvent { OrderId = Guid.NewGuid() })).ConfigureAwait(false);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var envelopes = store.GetAll();
        envelopes.Should().HaveCount(2);
        envelopes.Select(envelope => envelope.Handler).Should().BeEquivalentTo(
            typeof(SteadyReaction).FullName,
            typeof(FlakyReaction).FullName);
        envelopes.Select(envelope => envelope.Id).Should().OnlyHaveUniqueItems();
        envelopes.Should().Contain(envelope => envelope.Id == receipt.Id);
    }

    /// <summary>
    ///     Verifies a failing reaction retries alone while the healthy reaction publishes on its first attempt.
    /// </summary>
    [Fact]
    public async Task Drain_should_retry_only_the_failing_reaction()
    {
        var steady = new ReactionRecorder();
        var flaky = new FlakyReactionState(failuresBeforeSuccess: 2);
        var provider = BuildProvider(steady, flaky);
        var outbox = provider.GetRequiredService<IOutbox>();

        await outbox.EnqueueAsync(
            OutboxEnqueueItem<OrderEvent>.From(new OrderEvent { OrderId = Guid.NewGuid() })).ConfigureAwait(false);

        await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        store.GetAll().Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Published);
        steady.HandledCount.Should().Be(1);
        flaky.Attempts.Should().Be(3);
        flaky.HandledCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies an always-failing reaction dead letters alone and requeues back to dispatch without touching the published reaction.
    /// </summary>
    [Fact]
    public async Task DeadLetter_and_requeue_should_apply_per_handler()
    {
        var steady = new ReactionRecorder();
        var flaky = new FlakyReactionState(failuresBeforeSuccess: int.MaxValue);
        var provider = BuildProvider(steady, flaky, maxAttempts: 3);
        var outbox = provider.GetRequiredService<IOutbox>();

        await outbox.EnqueueAsync(
            OutboxEnqueueItem<OrderEvent>.From(new OrderEvent { OrderId = Guid.NewGuid() })).ConfigureAwait(false);

        await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var healthy = store.GetAll().Single(envelope => envelope.Handler == typeof(SteadyReaction).FullName);
        var dead = store.GetAll().Single(envelope => envelope.Handler == typeof(FlakyReaction).FullName);
        healthy.Status.Should().Be(OutboxStatus.Published);
        dead.Status.Should().Be(OutboxStatus.DeadLettered);
        steady.HandledCount.Should().Be(1);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var requeued = await manager.RequeueAsync([dead.Id]).ConfigureAwait(false);
        requeued.Should().Be(new RequeueResult(1, 1));

        flaky.FailuresBeforeSuccess = 0;
        await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        store.Get(dead.Id).Status.Should().Be(OutboxStatus.Published);
        store.Get(healthy.Id).Status.Should().Be(OutboxStatus.Published);
        steady.HandledCount.Should().Be(1);
        flaky.HandledCount.Should().Be(1);
    }

    /// <summary>
    ///     Verifies enqueueing an event with no subscribed main handler fails fast like the default in-process dispatch rule.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_without_a_subscribed_handler_should_throw()
    {
        var provider = BuildProvider();
        var outbox = provider.GetRequiredService<IOutbox>();

        var act = () => outbox.EnqueueAsync(
            OutboxEnqueueItem<UnobservedEvent>.From(new UnobservedEvent()),
            CancellationToken.None);

        (await act.Should().ThrowAsync<NoHandlerFoundException>().ConfigureAwait(false))
            .Which.MessageType.Should().Be<UnobservedEvent>();
    }

    /// <summary>
    ///     Verifies a caller idempotency key is derived per handler so per-handler envelopes deduplicate per handler.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_should_derive_idempotency_keys_per_handler()
    {
        var provider = BuildProvider();
        var outbox = provider.GetRequiredService<IOutbox>();

        await outbox.EnqueueAsync(
            OutboxEnqueueItem<OrderEvent>.WithIdempotency(new OrderEvent { OrderId = Guid.NewGuid() }, "order-1")).ConfigureAwait(false);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        store.GetAll().Select(envelope => envelope.IdempotencyKey).Should().BeEquivalentTo(
            $"order-1:{typeof(SteadyReaction).FullName}",
            $"order-1:{typeof(FlakyReaction).FullName}");
    }

    /// <summary>
    ///     Verifies a caller-supplied identifier derives deterministic per-handler identifiers, so a retried enqueue resolves to the same rows.
    /// </summary>
    [Fact]
    public async Task EnqueueAsync_should_replay_supplied_identity_to_the_same_per_handler_rows()
    {
        var provider = BuildProvider();
        var outbox = provider.GetRequiredService<IOutbox>();
        var messageId = Guid.NewGuid();
        var message = new OrderEvent { OrderId = Guid.NewGuid() };

        var first = await outbox.EnqueueAsync(OutboxEnqueueItem<OrderEvent>.WithIdentity(message, messageId)).ConfigureAwait(false);
        var second = await outbox.EnqueueAsync(OutboxEnqueueItem<OrderEvent>.WithIdentity(message, messageId)).ConfigureAwait(false);

        first.Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
        second.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
        second.Id.Should().Be(first.Id);
        provider.GetRequiredService<InMemoryOutboxStore>().GetAll().Should().HaveCount(2);
    }

    /// <summary>
    ///     Verifies pipeline observers still run on every targeted dispatch while each main handler runs only for its own envelope.
    /// </summary>
    [Fact]
    public async Task Targeted_dispatch_should_run_observers_on_every_envelope()
    {
        var observer = new CompletionObserver();
        var provider = new ServiceCollection()
            .AddSingleton(new ReactionRecorder())
            .AddSingleton(new FlakyReactionState(0))
            .AddSingleton(observer)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<SteadyReaction>();
                    builder.Register<FlakyReaction>();
                    builder.Register<CompletionObserverHandler>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.Contracts.Register<OrderEvent>("orders.events.per-handler");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "per-handler-test",
                        Retry = new RetryOptions
                        {
                            MaxAttempts = 5,
                            InitialDelay = TimeSpan.Zero,
                            UseJitter = false
                        }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                    builder.EnablePerHandlerDelivery();
                });
            })
            .BuildServiceProvider();

        var outbox = provider.GetRequiredService<IOutbox>();

        await outbox.EnqueueAsync(
            OutboxEnqueueItem<OrderEvent>.From(new OrderEvent { OrderId = Guid.NewGuid() })).ConfigureAwait(false);

        await ProcessorPassHelpers.DrainOutboxAsync(provider).ConfigureAwait(false);

        observer.Completions.Should().Be(2);
    }

    /// <summary>
    ///     Verifies batch enqueue flattens per-handler envelopes with one receipt per envelope.
    /// </summary>
    [Fact]
    public async Task EnqueueBatchAsync_should_flatten_per_handler_envelopes()
    {
        var provider = BuildProvider();
        var outbox = provider.GetRequiredService<IOutbox>();

        var receipts = await outbox.EnqueueBatchAsync(
            [OutboxEnqueueItem.From(new OrderEvent { OrderId = Guid.NewGuid() }, typeof(OrderEvent))]).ConfigureAwait(false);

        receipts.Should().HaveCount(2);
        provider.GetRequiredService<InMemoryOutboxStore>().GetAll().Should().HaveCount(2);
    }

    private static ServiceProvider BuildProvider(
        ReactionRecorder? steady = null,
        FlakyReactionState? flaky = null,
        int maxAttempts = 5)
    {
        return new ServiceCollection()
            .AddSingleton(steady ?? new ReactionRecorder())
            .AddSingleton(flaky ?? new FlakyReactionState(0))
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddEvents(builder =>
                {
                    builder.Register<SteadyReaction>();
                    builder.Register<FlakyReaction>();
                });

                registry.AddOutbox(builder =>
                {
                    builder.Contracts.Register<OrderEvent>("orders.events.per-handler");
                    builder.Contracts.Register<UnobservedEvent>("orders.events.unobserved");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "per-handler-test",
                        Retry = new RetryOptions
                        {
                            MaxAttempts = maxAttempts,
                            InitialDelay = TimeSpan.Zero,
                            UseJitter = false
                        }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                    builder.EnablePerHandlerDelivery();
                });
            })
            .BuildServiceProvider();
    }

    private sealed record OrderEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed record UnobservedEvent;

    /// <summary>
    ///     Counts handled events for the healthy reaction.
    /// </summary>
    private sealed class ReactionRecorder
    {
        /// <summary>
        ///     Gets or sets the number of handled events.
        /// </summary>
        public int HandledCount { get; set; }
    }

    /// <summary>
    ///     Fails a configured number of attempts before handling for the flaky reaction.
    /// </summary>
    private sealed class FlakyReactionState
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="FlakyReactionState" /> class.
        /// </summary>
        /// <param name="failuresBeforeSuccess">The number of attempts that throw before handling succeeds.</param>
        public FlakyReactionState(int failuresBeforeSuccess)
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
                throw new InvalidOperationException("Flaky reaction failure.");
            }

            HandledCount++;
        }
    }

    /// <summary>
    ///     The healthy reaction that always handles.
    /// </summary>
    private sealed class SteadyReaction : IEventHandler<OrderEvent>
    {
        private readonly ReactionRecorder _recorder;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SteadyReaction" /> class.
        /// </summary>
        /// <param name="recorder">The recorder that counts handled events.</param>
        public SteadyReaction(ReactionRecorder recorder)
        {
            _recorder = recorder;
        }

        /// <inheritdoc />
        public Task HandleAsync(OrderEvent message, CancellationToken cancellationToken = default)
        {
            _recorder.HandledCount++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Counts completion observations across targeted dispatches.
    /// </summary>
    private sealed class CompletionObserver
    {
        /// <summary>
        ///     Gets or sets the number of observed completions.
        /// </summary>
        public int Completions { get; set; }
    }

    /// <summary>
    ///     Observes every mediation of the order event at the completion stage.
    /// </summary>
    private sealed class CompletionObserverHandler : IEventCompletionHandler<OrderEvent>
    {
        private readonly CompletionObserver _observer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CompletionObserverHandler" /> class.
        /// </summary>
        /// <param name="observer">The observer that counts completions.</param>
        public CompletionObserverHandler(CompletionObserver observer)
        {
            _observer = observer;
        }

        /// <inheritdoc />
        public Task HandleCompletionAsync(
            MessageCompletionContext<OrderEvent> context,
            CancellationToken cancellationToken)
        {
            _observer.Completions++;
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     The flaky reaction that fails a configured number of attempts before handling.
    /// </summary>
    private sealed class FlakyReaction : IEventHandler<OrderEvent>
    {
        private readonly FlakyReactionState _state;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FlakyReaction" /> class.
        /// </summary>
        /// <param name="state">The state that fails attempts before handling.</param>
        public FlakyReaction(FlakyReactionState state)
        {
            _state = state;
        }

        /// <inheritdoc />
        public Task HandleAsync(OrderEvent message, CancellationToken cancellationToken = default)
        {
            _state.Attempt();
            return Task.CompletedTask;
        }
    }
}
