using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.DomainEvents;

/// <summary>
///     Verifies collected domain events are staged into the transactional outbox when a mediation completes successfully, before the unit of work commits.
/// </summary>
[Collection("Sequential")]
public sealed class DomainEventStagingTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies collected domain events are staged into the transactional outbox when the mediation succeeds.
    /// </summary>
    [Fact]
    public async Task PublishAsync_should_stage_collected_events_on_success()
    {
        var provider = BuildProvider();
        var mediator = provider.GetRequiredService<IEventMediator>();

        await mediator.PublishAsync(new PlaceOrderTrigger { OrderId = Guid.NewGuid() }).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.order-placed" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);
    }

    /// <summary>
    ///     Verifies a failed mediation stages nothing.
    /// </summary>
    [Fact]
    public async Task PublishAsync_should_stage_nothing_on_failure()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<FailingCollector>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderPlacedStagedEvent>("tests.events.order-placed");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        var provider = services.BuildServiceProvider();

        var mediator = provider.GetRequiredService<IEventMediator>();
        var act = () => mediator.PublishAsync(new FailingTrigger());

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies the staging handler runs before the unit of work band, so staged rows join the commit.
    /// </summary>
    [Fact]
    public async Task Staging_should_run_before_the_unit_of_work_band()
    {
        var probe = new UnitOfWorkBandProbe();
        var services = new ServiceCollection();
        services.AddSingleton(probe);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder =>
            {
                builder.Register<CollectingHandler>();
                builder.Register<UnitOfWorkBandObserver>();
            });

            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderPlacedStagedEvent>("tests.events.order-placed");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new PlaceOrderTrigger { OrderId = Guid.NewGuid() }).ConfigureAwait(false);

        probe.StagedCountWhenCommitRuns.Should().Be(2);
    }

    /// <summary>
    ///     Verifies a mediation that collects nothing never touches the transactional outbox.
    /// </summary>
    [Fact]
    public async Task PublishAsync_without_collected_events_should_not_require_a_transactional_outbox()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<QuietHandler>());
            registry.AddOutbox(builder =>
            {
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new QuietTrigger()).ConfigureAwait(false);
    }

    /// <summary>
    ///     Verifies collected events without a registered transactional outbox fail with staging guidance.
    /// </summary>
    [Fact]
    public async Task PublishAsync_with_collected_events_but_no_transactional_outbox_should_throw()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<CollectingHandler>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderPlacedStagedEvent>("tests.events.order-placed");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new PlaceOrderTrigger { OrderId = Guid.NewGuid() });

        (await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false))
            .WithMessage("*StageCollectedEvents*");
    }

    /// <summary>
    ///     Verifies staged envelopes stamp ambient trace when the ambient trace option is on.
    /// </summary>
    [Fact]
    public async Task Staging_should_stamp_ambient_trace_when_opted_in()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<CollectingHandler>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderPlacedStagedEvent>("tests.events.order-placed");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
                builder.UseAmbientTrace();
            });
        });
        services.AddTestTransactionalOutbox();
        var provider = services.BuildServiceProvider();

        var settings = new EventMediationSettings
        {
            Items = new Dictionary<string, object>
            {
                [MessageTraceContextKeys.CorrelationId] = "correlation-9",
                [MessageTraceContextKeys.CausationId] = "causation-3"
            }
        };

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new PlaceOrderTrigger { OrderId = Guid.NewGuid() }, settings).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.order-placed" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(envelope =>
            envelope.CorrelationId == "correlation-9" && envelope.CausationId == "causation-3");
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<CollectingHandler>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderPlacedStagedEvent>("tests.events.order-placed");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();

        return services.BuildServiceProvider();
    }

    private sealed record PlaceOrderTrigger
    {
        public Guid OrderId { get; init; }
    }

    private sealed record FailingTrigger;

    private sealed record QuietTrigger;

    private sealed record OrderPlacedStagedEvent
    {
        public Guid OrderId { get; init; }

        public int Sequence { get; init; }
    }

    /// <summary>
    ///     Collects two staged events during handling.
    /// </summary>
    private sealed class CollectingHandler : IEventHandler<PlaceOrderTrigger>
    {
        private readonly IDomainEventCollector _collector;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CollectingHandler" /> class.
        /// </summary>
        /// <param name="collector">The mediation-scoped domain event collector.</param>
        public CollectingHandler(IDomainEventCollector collector)
        {
            _collector = collector;
        }

        /// <inheritdoc />
        public Task HandleAsync(PlaceOrderTrigger message, CancellationToken cancellationToken = default)
        {
            _collector.Collect(new OrderPlacedStagedEvent { OrderId = message.OrderId, Sequence = 1 });
            _collector.Collect(new OrderPlacedStagedEvent { OrderId = message.OrderId, Sequence = 2 });
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Collects one staged event and then fails the mediation.
    /// </summary>
    private sealed class FailingCollector : IEventHandler<FailingTrigger>
    {
        private readonly IDomainEventCollector _collector;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FailingCollector" /> class.
        /// </summary>
        /// <param name="collector">The mediation-scoped domain event collector.</param>
        public FailingCollector(IDomainEventCollector collector)
        {
            _collector = collector;
        }

        /// <inheritdoc />
        public Task HandleAsync(FailingTrigger message, CancellationToken cancellationToken = default)
        {
            _collector.Collect(new OrderPlacedStagedEvent { OrderId = Guid.NewGuid(), Sequence = 1 });
            throw new InvalidOperationException("Collector failure.");
        }
    }

    /// <summary>
    ///     Handles without collecting anything.
    /// </summary>
    private sealed class QuietHandler : IEventHandler<QuietTrigger>
    {
        /// <inheritdoc />
        public Task HandleAsync(QuietTrigger message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     Records how many staged rows exist when the unit of work band runs.
    /// </summary>
    private sealed class UnitOfWorkBandProbe
    {
        /// <summary>
        ///     Gets or sets the staged row count observed by the unit of work band observer.
        /// </summary>
        public int StagedCountWhenCommitRuns { get; set; } = -1;
    }

    /// <summary>
    ///     Observes the mediation at the unit of work band, after LiteBus completion handlers.
    /// </summary>
    [HandlerPriority(HandlerPriorities.UnitOfWork)]
    private sealed class UnitOfWorkBandObserver : IEventCompletionHandler<PlaceOrderTrigger>
    {
        private readonly UnitOfWorkBandProbe _probe;
        private readonly TestOutboxStore _store;

        /// <summary>
        ///     Initializes a new instance of the <see cref="UnitOfWorkBandObserver" /> class.
        /// </summary>
        /// <param name="probe">The probe that records the staged row count.</param>
        /// <param name="store">The shared test store holding staged rows.</param>
        public UnitOfWorkBandObserver(UnitOfWorkBandProbe probe, TestOutboxStore store)
        {
            _probe = probe;
            _store = store;
        }

        /// <inheritdoc />
        public async Task HandleCompletionAsync(
            MessageCompletionContext<PlaceOrderTrigger> context,
            CancellationToken cancellationToken)
        {
            var page = await _store.QueryAsync(
                new OutboxMessageFilter { ContractName = "tests.events.order-placed" },
                new OutboxMessagePageRequest { PageSize = 10 },
                cancellationToken).ConfigureAwait(false);

            _probe.StagedCountWhenCommitRuns = page.Items.Count;
        }
    }
}
