using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.DomainEvents;

namespace LiteBus.Outbox.UnitTests.DomainEvents;

/// <summary>
///     Verifies harvest registrations given as a type or a predicate match open generic bases, closed types, and naming conventions.
/// </summary>
public sealed class DomainEventOpenGenericHarvestTests
{
    /// <summary>
    ///     Verifies one open generic base registration harvests every closed aggregate built on it.
    /// </summary>
    [Fact]
    public void Type_overload_should_harvest_every_closed_aggregate_built_on_an_open_generic_base()
    {
        var builder = new OutboxModuleBuilder();
        builder.HarvestDomainEvents(
            typeof(AggregateRoot<>),
            static aggregate => ((IBufferedEvents) aggregate).Buffered,
            static aggregate => ((IBufferedEvents) aggregate).Buffered.Clear());
        var accessors = new DomainEventAccessors(builder.CollectDomainEventAccessors());
        var collector = new DomainEventCollector();
        var orderAggregate = new OrderAggregate();
        var customerAggregate = new CustomerAggregate();
        var first = new HarvestedEvent { Sequence = 1 };
        var second = new HarvestedEvent { Sequence = 2 };
        orderAggregate.Buffered.Add(first);
        customerAggregate.Buffered.Add(second);

        accessors.Harvest(orderAggregate, collector);
        accessors.Harvest(customerAggregate, collector);

        collector.Drain().Should().Equal(first, second);
        orderAggregate.Buffered.Should().BeEmpty();
        customerAggregate.Buffered.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies the type overload matches a closed type like the generic overload.
    /// </summary>
    [Fact]
    public void Type_overload_should_match_closed_types_like_the_generic_overload()
    {
        var builder = new OutboxModuleBuilder();
#pragma warning disable CA2263 // This test covers the non-generic overload on purpose.
        builder.HarvestDomainEvents(
            typeof(StandaloneAggregate),
            static aggregate => ((StandaloneAggregate) aggregate).PendingEvents,
            static aggregate => ((StandaloneAggregate) aggregate).PendingEvents.Clear());
#pragma warning restore CA2263
        var accessors = new DomainEventAccessors(builder.CollectDomainEventAccessors());
        var collector = new DomainEventCollector();
        var aggregate = new StandaloneAggregate();
        var placed = new HarvestedEvent { Sequence = 1 };
        aggregate.PendingEvents.Add(placed);

        accessors.Harvest(aggregate, collector);

        collector.Drain().Should().Equal(placed);
        aggregate.PendingEvents.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies one open generic interface registration harvests every closed aggregate implementing it.
    /// </summary>
    [Fact]
    public void Type_overload_should_harvest_closed_aggregates_built_on_an_open_generic_interface()
    {
        var builder = new OutboxModuleBuilder();
        builder.HarvestDomainEvents(
            typeof(IAggregateStream<>),
            static aggregate => ((IBufferedEvents) aggregate).Buffered,
            static aggregate => ((IBufferedEvents) aggregate).Buffered.Clear());
        var accessors = new DomainEventAccessors(builder.CollectDomainEventAccessors());
        var collector = new DomainEventCollector();
        var aggregate = new ShipmentAggregate();
        var placed = new HarvestedEvent { Sequence = 1 };
        aggregate.Buffered.Add(placed);

        accessors.Harvest(aggregate, collector);

        collector.Drain().Should().Equal(placed);
        aggregate.Buffered.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a predicate harvests aggregates with no common base and ignores other entities.
    /// </summary>
    [Fact]
    public void Predicate_should_harvest_matching_types_and_ignore_others()
    {
        var builder = new OutboxModuleBuilder();
        builder.HarvestDomainEventsWhere(
            static type => type.Name.EndsWith("Aggregate", StringComparison.Ordinal),
            static aggregate => ((IBufferedEvents) aggregate).Buffered,
            static aggregate => ((IBufferedEvents) aggregate).Buffered.Clear());
        var accessors = new DomainEventAccessors(builder.CollectDomainEventAccessors());
        var collector = new DomainEventCollector();
        var aggregate = new OrderAggregate();
        var placed = new HarvestedEvent { Sequence = 1 };
        aggregate.Buffered.Add(placed);

        accessors.Harvest(aggregate, collector);
        accessors.Harvest(new UnrelatedState(), collector);

        collector.Drain().Should().Equal(placed);
        aggregate.Buffered.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies an exact type match wins over a predicate regardless of registration order.
    /// </summary>
    [Fact]
    public void Exact_type_match_should_win_over_a_predicate()
    {
        var builder = new OutboxModuleBuilder();
        builder.HarvestDomainEventsWhere(
            static _ => true,
            static aggregate => ((DualSourceAggregate) aggregate).Extra,
            static aggregate => ((DualSourceAggregate) aggregate).Extra.Clear());
        builder.HarvestDomainEvents<DualSourceAggregate>(
            static aggregate => aggregate.Buffered,
            static aggregate => aggregate.Buffered.Clear());
        var accessors = new DomainEventAccessors(builder.CollectDomainEventAccessors());
        var collector = new DomainEventCollector();
        var aggregate = new DualSourceAggregate();
        var buffered = new HarvestedEvent { Sequence = 1 };
        var extra = new HarvestedEvent { Sequence = 2 };
        aggregate.Buffered.Add(buffered);
        aggregate.Extra.Add(extra);

        accessors.Harvest(aggregate, collector);

        collector.Drain().Should().Equal(buffered);
        aggregate.Buffered.Should().BeEmpty();
        aggregate.Extra.Should().Equal(extra);
    }

    /// <summary>
    ///     Verifies the first registration in order wins when several non-exact matches overlap.
    /// </summary>
    [Fact]
    public void First_registration_in_order_should_win_for_overlapping_matches()
    {
        var builder = new OutboxModuleBuilder();
        builder.HarvestDomainEventsWhere(
            static _ => true,
            static aggregate => ((DualSourceAggregate) aggregate).Extra,
            static aggregate => ((DualSourceAggregate) aggregate).Extra.Clear());
        builder.HarvestDomainEvents(
            typeof(AggregateRoot<>),
            static aggregate => ((IBufferedEvents) aggregate).Buffered,
            static aggregate => ((IBufferedEvents) aggregate).Buffered.Clear());
        var accessors = new DomainEventAccessors(builder.CollectDomainEventAccessors());
        var collector = new DomainEventCollector();
        var aggregate = new DualSourceAggregate();
        var buffered = new HarvestedEvent { Sequence = 1 };
        var extra = new HarvestedEvent { Sequence = 2 };
        aggregate.Buffered.Add(buffered);
        aggregate.Extra.Add(extra);

        accessors.Harvest(aggregate, collector);

        collector.Drain().Should().Equal(extra);
        aggregate.Extra.Should().BeEmpty();
        aggregate.Buffered.Should().Equal(buffered);
    }

    /// <summary>
    ///     An event harvested from an aggregate during matching tests.
    /// </summary>
    private sealed record HarvestedEvent
    {
        /// <summary>
        ///     Gets the event sequence.
        /// </summary>
        public int Sequence { get; init; }
    }

    /// <summary>
    ///     Reads buffered events without naming the generic base.
    /// </summary>
    private interface IBufferedEvents
    {
        /// <summary>
        ///     Gets the buffered domain events.
        /// </summary>
        List<object> Buffered { get; }
    }

    /// <summary>
    ///     An open generic aggregate base shared by test aggregates.
    /// </summary>
    /// <typeparam name="TId">The identifier type closing the aggregate.</typeparam>
    private abstract class AggregateRoot<TId> : IBufferedEvents
    {
        /// <inheritdoc />
        public List<object> Buffered { get; } = [];
    }

    /// <summary>
    ///     An aggregate built on the open generic base with one identifier type.
    /// </summary>
    private sealed class OrderAggregate : AggregateRoot<Guid>
    {
    }

    /// <summary>
    ///     An aggregate built on the open generic base with another identifier type.
    /// </summary>
    private sealed class CustomerAggregate : AggregateRoot<int>
    {
    }

    /// <summary>
    ///     An open generic aggregate contract shared by test aggregates.
    /// </summary>
    /// <typeparam name="TId">The identifier type closing the aggregate.</typeparam>
    private interface IAggregateStream<TId>
    {
        /// <summary>
        ///     Gets the stream identifier.
        /// </summary>
        TId StreamId { get; }
    }

    /// <summary>
    ///     An aggregate implementing the open generic contract.
    /// </summary>
    private sealed class ShipmentAggregate : IAggregateStream<Guid>, IBufferedEvents
    {
        /// <inheritdoc />
        public Guid StreamId => Guid.Empty;

        /// <inheritdoc />
        public List<object> Buffered { get; } = [];
    }

    /// <summary>
    ///     An aggregate with no generic base, harvested through a closed registration.
    /// </summary>
    private sealed class StandaloneAggregate
    {
        /// <summary>
        ///     Gets the buffered domain events.
        /// </summary>
        public List<object> PendingEvents { get; } = [];
    }

    /// <summary>
    ///     An entity the test predicate never matches.
    /// </summary>
    private sealed class UnrelatedState
    {
    }

    /// <summary>
    ///     An aggregate exposing two event sources, proving which accessor a match used.
    /// </summary>
    private sealed class DualSourceAggregate : AggregateRoot<Guid>
    {
        /// <summary>
        ///     Gets the second event source read only by the predicate accessors.
        /// </summary>
        public List<object> Extra { get; } = [];
    }
}
