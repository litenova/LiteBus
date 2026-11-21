using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.DomainEvents;
using LiteBus.Outbox.Storage.InMemory;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests.DomainEvents;

/// <summary>
///     Verifies registered harvesters run before collected domain events stage, and that configured accessors and the default entity contract harvest tracked aggregates.
/// </summary>
[Collection("Sequential")]
public sealed class DomainEventHarvestTests : LiteBusTestBase
{
    /// <summary>
    ///     Verifies a registered harvester moves its events into the collector before the staging drain, so they stage on success.
    /// </summary>
    [Fact]
    public async Task PublishAsync_should_stage_harvested_events_on_success()
    {
        var harvester = new RecordingHarvester(
            new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 1 },
            new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 2 });
        var provider = BuildProvider(harvester);
        var mediator = provider.GetRequiredService<IEventMediator>();

        await mediator.PublishAsync(new HarvestTrigger()).ConfigureAwait(false);

        harvester.Ran.Should().BeTrue();

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.order-placed" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(2);
        page.Items.Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Pending);
    }

    /// <summary>
    ///     Verifies every registered harvester runs and its events stage.
    /// </summary>
    [Fact]
    public async Task PublishAsync_should_run_every_registered_harvester()
    {
        var first = new RecordingHarvester(new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 1 });
        var second = new RecordingHarvester(new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 2 });
        var provider = BuildProvider(first, second);

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new HarvestTrigger()).ConfigureAwait(false);

        first.Ran.Should().BeTrue();
        second.Ran.Should().BeTrue();

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.order-placed" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(2);
    }

    /// <summary>
    ///     Verifies handler-collected events and harvested events stage together.
    /// </summary>
    [Fact]
    public async Task PublishAsync_should_stage_collected_and_harvested_events_together()
    {
        var harvester = new RecordingHarvester(new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 9 });
        var services = new ServiceCollection();
        services.AddScoped<IDomainEventHarvester>(_ => harvester);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<CollectingHandler>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderPlacedHarvestEvent>("tests.events.order-placed");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        var provider = services.BuildServiceProvider();

        await provider.GetRequiredService<IEventMediator>()
            .PublishAsync(new HarvestTrigger()).ConfigureAwait(false);

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter { ContractName = "tests.events.order-placed" },
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().HaveCount(2);
    }

    /// <summary>
    ///     Verifies a failed mediation runs no harvester and stages nothing.
    /// </summary>
    [Fact]
    public async Task PublishAsync_should_not_run_harvesters_on_failure()
    {
        var harvester = new RecordingHarvester(new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 1 });
        var services = new ServiceCollection();
        services.AddScoped<IDomainEventHarvester>(_ => harvester);
        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<FailingCollector>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderPlacedHarvestEvent>("tests.events.order-placed");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();
        var provider = services.BuildServiceProvider();

        var act = () => provider.GetRequiredService<IEventMediator>().PublishAsync(new FailingHarvestTrigger());

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        harvester.Ran.Should().BeFalse();

        var manager = provider.GetRequiredService<IOutboxManager>();
        var page = await manager.QueryAsync(
            new OutboxMessageFilter(),
            new OutboxMessagePageRequest { PageSize = 10 }).ConfigureAwait(false);

        page.Items.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a configured accessor pair reads the aggregate events and clears them.
    /// </summary>
    [Fact]
    public void Harvest_should_read_configured_accessors_and_clear_the_aggregate()
    {
        var builder = new OutboxModuleBuilder();
        builder.HarvestDomainEvents<OrderAggregate>(
            aggregate => aggregate.PendingEvents,
            aggregate => aggregate.PendingEvents.Clear());
        var accessors = new DomainEventAccessors(builder.CollectDomainEventAccessors());
        var collector = new DomainEventCollector();
        var aggregate = new OrderAggregate();
        var first = new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 1 };
        var second = new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 2 };
        aggregate.PendingEvents.Add(first);
        aggregate.PendingEvents.Add(second);

        accessors.Harvest(aggregate, collector);

        collector.Drain().Should().Equal(first, second);
        aggregate.PendingEvents.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies an aggregate implementing the domain event contract harvests by default without configuration.
    /// </summary>
    [Fact]
    public void Harvest_should_harvest_contract_aggregates_by_default_without_configuration()
    {
        var accessors = new DomainEventAccessors([]);
        var collector = new DomainEventCollector();
        var aggregate = new CustomerAggregate();
        var placed = new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 1 };
        aggregate.Raise(placed);

        accessors.Harvest(aggregate, collector);

        collector.Drain().Should().Equal(placed);
        aggregate.DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a configured accessor wins over the default contract for its aggregate type.
    /// </summary>
    [Fact]
    public void Harvest_should_prefer_the_configured_accessor_over_the_default_contract()
    {
        var builder = new OutboxModuleBuilder();
        builder.HarvestDomainEvents<DualAggregate>(
            aggregate => aggregate.CustomEvents,
            aggregate => aggregate.CustomEvents.Clear());
        var accessors = new DomainEventAccessors(builder.CollectDomainEventAccessors());
        var collector = new DomainEventCollector();
        var aggregate = new DualAggregate();
        var custom = new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 1 };
        var contract = new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 2 };
        aggregate.CustomEvents.Add(custom);
        aggregate.Raise(contract);

        accessors.Harvest(aggregate, collector);

        collector.Drain().Should().Equal(custom);
        aggregate.CustomEvents.Should().BeEmpty();
        aggregate.DomainEvents.Should().Equal(contract);
    }

    /// <summary>
    ///     Verifies an entity with no accessor and no contract is ignored.
    /// </summary>
    [Fact]
    public void Harvest_should_ignore_entities_without_accessors_or_contract()
    {
        var accessors = new DomainEventAccessors([]);
        var collector = new DomainEventCollector();

        accessors.Harvest(new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 1 }, collector);

        collector.Drain().Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a derived aggregate harvests through the accessor configured for its base type.
    /// </summary>
    [Fact]
    public void Harvest_should_match_derived_aggregates_through_the_configured_base_type()
    {
        var builder = new OutboxModuleBuilder();
        builder.HarvestDomainEvents<BaseAggregate>(
            aggregate => aggregate.PendingEvents,
            aggregate => aggregate.PendingEvents.Clear());
        var accessors = new DomainEventAccessors(builder.CollectDomainEventAccessors());
        var collector = new DomainEventCollector();
        var aggregate = new DerivedAggregate();
        var placed = new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 1 };
        aggregate.PendingEvents.Add(placed);

        accessors.Harvest(aggregate, collector);

        collector.Drain().Should().Equal(placed);
        aggregate.PendingEvents.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies registering the same aggregate type twice replaces the previous accessor pair.
    /// </summary>
    [Fact]
    public void HarvestDomainEvents_should_replace_the_accessor_on_repeat_registration()
    {
        var builder = new OutboxModuleBuilder();
        builder.HarvestDomainEvents<OrderAggregate>(
            aggregate => aggregate.PendingEvents,
            aggregate => aggregate.PendingEvents.Clear());
        builder.HarvestDomainEvents<OrderAggregate>(
            aggregate => aggregate.PendingEvents,
            _ => { });

        builder.CollectDomainEventAccessors().Should().HaveCount(1);

        var accessors = new DomainEventAccessors(builder.CollectDomainEventAccessors());
        var collector = new DomainEventCollector();
        var aggregate = new OrderAggregate();
        aggregate.PendingEvents.Add(new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 1 });

        accessors.Harvest(aggregate, collector);

        collector.Drain().Should().HaveCount(1);
        aggregate.PendingEvents.Should().HaveCount(1);
    }

    /// <summary>
    ///     Verifies null accessor delegates are rejected.
    /// </summary>
    [Fact]
    public void HarvestDomainEvents_should_reject_null_accessors()
    {
        var builder = new OutboxModuleBuilder();

        var actEvents = () => builder.HarvestDomainEvents<OrderAggregate>(null!, aggregate => aggregate.PendingEvents.Clear());
        var actClear = () => builder.HarvestDomainEvents<OrderAggregate>(aggregate => aggregate.PendingEvents, null!);

        actEvents.Should().Throw<ArgumentNullException>();
        actClear.Should().Throw<ArgumentNullException>();
    }

    private static ServiceProvider BuildProvider(params IDomainEventHarvester[] harvesters)
    {
        var services = new ServiceCollection();

        foreach (var harvester in harvesters)
        {
            services.AddScoped<IDomainEventHarvester>(_ => harvester);
        }

        services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddEvents(builder => builder.Register<QuietHandler>());
            registry.AddOutbox(builder =>
            {
                builder.Contracts.Register<OrderPlacedHarvestEvent>("tests.events.order-placed");
                builder.UseInMemoryStorage();
                builder.StageCollectedEvents();
            });
        });
        services.AddTestTransactionalOutbox();

        return services.BuildServiceProvider();
    }

    private sealed record HarvestTrigger;

    private sealed record FailingHarvestTrigger;

    private sealed record OrderPlacedHarvestEvent
    {
        public Guid OrderId { get; init; }

        public int Sequence { get; init; }
    }

    private sealed class RecordingHarvester : IDomainEventHarvester
    {
        private readonly object[] _events;

        public RecordingHarvester(params object[] events)
        {
            _events = events;
        }

        public bool Ran { get; private set; }

        public void HarvestInto(IDomainEventCollector collector)
        {
            Ran = true;

            foreach (var domainEvent in _events)
            {
                collector.Collect(domainEvent);
            }
        }
    }

    private sealed class CollectingHandler : IEventHandler<HarvestTrigger>
    {
        private readonly IDomainEventCollector _collector;

        public CollectingHandler(IDomainEventCollector collector)
        {
            _collector = collector;
        }

        public Task HandleAsync(HarvestTrigger message, CancellationToken cancellationToken = default)
        {
            _collector.Collect(new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 7 });
            return Task.CompletedTask;
        }
    }

    private sealed class FailingCollector : IEventHandler<FailingHarvestTrigger>
    {
        private readonly IDomainEventCollector _collector;

        public FailingCollector(IDomainEventCollector collector)
        {
            _collector = collector;
        }

        public Task HandleAsync(FailingHarvestTrigger message, CancellationToken cancellationToken = default)
        {
            _collector.Collect(new OrderPlacedHarvestEvent { OrderId = Guid.NewGuid(), Sequence = 1 });
            throw new InvalidOperationException("Collector failure.");
        }
    }

    private sealed class QuietHandler : IEventHandler<HarvestTrigger>
    {
        public Task HandleAsync(HarvestTrigger message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class OrderAggregate
    {
        public List<object> PendingEvents { get; } = [];
    }

    private sealed class CustomerAggregate : IHasDomainEvents
    {
        private readonly List<object> _events = [];

        public IReadOnlyCollection<object> DomainEvents => _events;

        public void Raise(object domainEvent)
        {
            _events.Add(domainEvent);
        }

        public void ClearDomainEvents()
        {
            _events.Clear();
        }
    }

    private sealed class DualAggregate : IHasDomainEvents
    {
        private readonly List<object> _events = [];

        public List<object> CustomEvents { get; } = [];

        public IReadOnlyCollection<object> DomainEvents => _events;

        public void Raise(object domainEvent)
        {
            _events.Add(domainEvent);
        }

        public void ClearDomainEvents()
        {
            _events.Clear();
        }
    }

    private class BaseAggregate
    {
        public List<object> PendingEvents { get; } = [];
    }

    private sealed class DerivedAggregate : BaseAggregate
    {
    }
}
