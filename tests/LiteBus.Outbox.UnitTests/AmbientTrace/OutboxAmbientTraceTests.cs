using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.AmbientTrace;

/// <summary>
///     Verifies the ambient trace option stamps ambient trace onto enqueued envelopes whose items carry none.
/// </summary>
[Collection("Sequential")]
public sealed class OutboxAmbientTraceTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies the factory stamps ambient trace when opted in and the item trace is none.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_opted_in_and_item_trace_is_none_should_stamp_ambient_trace()
    {
        var factory = CreateFactory(new OutboxEnqueueOptions { UseAmbientTrace = true });
        var items = new Dictionary<string, object>();
        MessageProcessorDiagnostics.ApplyTraceMetadata(items, "correlation-1", "causation-1", null);

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(items)))
        {
            var envelope = await factory.CreateAsync(OutboxEnqueueItem<TracedEvent>.From(new TracedEvent())).ConfigureAwait(false);

            envelope.CorrelationId.Should().Be("correlation-1");
            envelope.CausationId.Should().Be("causation-1");
        }
    }

    /// <summary>
    ///     Verifies the factory ignores ambient trace when the option is off.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_not_opted_in_should_ignore_ambient_trace()
    {
        var factory = CreateFactory(new OutboxEnqueueOptions());
        var items = new Dictionary<string, object>();
        MessageProcessorDiagnostics.ApplyTraceMetadata(items, "correlation-1", "causation-1", null);

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(items)))
        {
            var envelope = await factory.CreateAsync(OutboxEnqueueItem<TracedEvent>.From(new TracedEvent())).ConfigureAwait(false);

            envelope.CorrelationId.Should().BeNull();
            envelope.CausationId.Should().BeNull();
        }
    }

    /// <summary>
    ///     Verifies an explicit item trace wins over ambient trace when opted in.
    /// </summary>
    [Fact]
    public async Task CreateAsync_when_item_trace_is_explicit_should_keep_it()
    {
        var factory = CreateFactory(new OutboxEnqueueOptions { UseAmbientTrace = true });
        var items = new Dictionary<string, object>();
        MessageProcessorDiagnostics.ApplyTraceMetadata(items, "correlation-ambient", "causation-ambient", null);

        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(items)))
        {
            var item = OutboxEnqueueItem<TracedEvent>.From(
                new TracedEvent(),
                OutboxEnqueueMetadata.Immediate with { Trace = new MessageTrace.Correlated("correlation-explicit") });
            var envelope = await factory.CreateAsync(item).ConfigureAwait(false);

            envelope.CorrelationId.Should().Be("correlation-explicit");
            envelope.CausationId.Should().BeNull();
        }
    }

    /// <summary>
    ///     Verifies an enqueue inside a traced mediation stamps ambient trace end to end when opted in.
    /// </summary>
    [Fact]
    public async Task Enqueue_inside_a_traced_mediation_should_stamp_ambient_trace_when_opted_in()
    {
        var provider = BuildProvider(useAmbientTrace: true);
        var settings = new EventMediationSettings
        {
            Items = new Dictionary<string, object>
            {
                [MessageTraceContextKeys.CorrelationId] = "correlation-7",
                [MessageTraceContextKeys.CausationId] = "causation-7"
            }
        };

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new EnqueueTrigger(), settings).ConfigureAwait(false);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var envelope = store.GetAll().Should().ContainSingle().Subject;
        envelope.CorrelationId.Should().Be("correlation-7");
        envelope.CausationId.Should().Be("causation-7");
    }

    /// <summary>
    ///     Verifies an enqueue inside a traced mediation stays untraced end to end without the option.
    /// </summary>
    [Fact]
    public async Task Enqueue_inside_a_traced_mediation_should_stay_untraced_without_the_option()
    {
        var provider = BuildProvider(useAmbientTrace: false);
        var settings = new EventMediationSettings
        {
            Items = new Dictionary<string, object>
            {
                [MessageTraceContextKeys.CorrelationId] = "correlation-7",
                [MessageTraceContextKeys.CausationId] = "causation-7"
            }
        };

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new EnqueueTrigger(), settings).ConfigureAwait(false);

        var store = provider.GetRequiredService<InMemoryOutboxStore>();
        var envelope = store.GetAll().Should().ContainSingle().Subject;
        envelope.CorrelationId.Should().BeNull();
        envelope.CausationId.Should().BeNull();
    }

    private static OutboxEnvelopeFactory CreateFactory(OutboxEnqueueOptions options)
    {
        var contracts = new MessageContractRegistry();
        contracts.Register<TracedEvent>("tests.events.traced");

        return new OutboxEnvelopeFactory(
            contracts,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System,
            null,
            null,
            null,
            options);
    }

    private static ServiceProvider BuildProvider(bool useAmbientTrace)
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<EnqueueingHandler>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<TracedEvent>("tests.events.traced");
                builder.UseInMemoryStorage();

                if (useAmbientTrace)
                {
                    builder.UseAmbientTrace();
                }
            });
        });

        return services.BuildServiceProvider();
    }

    private sealed record TracedEvent;

    private sealed record EnqueueTrigger;

    /// <summary>
    ///     Enqueues one event through the singleton outbox inside the mediation.
    /// </summary>
    private sealed class EnqueueingHandler : IEventHandler<EnqueueTrigger>
    {
        private readonly IOutbox _outbox;

        /// <summary>
        ///     Initializes a new instance of the <see cref="EnqueueingHandler" /> class.
        /// </summary>
        /// <param name="outbox">The outbox writer.</param>
        public EnqueueingHandler(IOutbox outbox)
        {
            _outbox = outbox;
        }

        /// <inheritdoc />
        public Task HandleAsync(EnqueueTrigger message, CancellationToken cancellationToken = default)
        {
            return _outbox.EnqueueAsync(OutboxEnqueueItem<TracedEvent>.From(new TracedEvent()), cancellationToken);
        }
    }

    private sealed class TestExecutionContext : IExecutionContext
    {
        public TestExecutionContext(IDictionary<string, object> items)
        {
            Items = items;
        }

        public CancellationToken CancellationToken => CancellationToken.None;

        public DateTimeOffset HandledAt { get; } = DateTimeOffset.UnixEpoch;

        public IDictionary<string, object> Items { get; }

        public IHandleContextData Data { get; } = new HandleContextData();

        public IReadOnlyCollection<string> Tags { get; } = [];

        public object? MessageResult { get; set; }

        public bool PostHandlersSuppressed { get; private set; }

        public void SuppressPostHandlers()
        {
            PostHandlersSuppressed = true;
        }
    }
}
