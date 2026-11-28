using System.Data;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.DomainEvents;
using LiteBus.Outbox.Storage.Marten.DomainEvents;
using Marten;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Storage.UnitTests.Marten;

/// <summary>
///     Verifies the Marten harvester moves domain events from pending deletions that carry a document, and skips
///     deletions without one. These tests queue session operations only and never touch the database.
/// </summary>
public sealed class MartenDomainEventHarvestDeletionTests
{
    /// <summary>
    ///     Verifies a document deleted with its instance harvests its events and clears them.
    /// </summary>
    [Fact]
    public void Deleted_document_harvests_its_events_and_clears_them()
    {
        using var store = CreateDocumentStore();
        using var session = store.LightweightSession(IsolationLevel.ReadCommitted);
        var accessors = CreateAccessors();
        var collector = new RecordingCollector();
        var document = new OrderDocument { Id = Guid.NewGuid() };
        var placed = new OrderPlacedEvent { OrderId = document.Id };
        document.Raise(placed);
        session.Delete(document);

        new MartenDomainEventHarvester(session, accessors).HarvestInto(collector);

        collector.Collected.Should().Equal(placed);
        document.DomainEvents.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a deleted document read through configured accessors harvests its events and clears them.
    /// </summary>
    [Fact]
    public void Deleted_document_with_configured_accessors_harvests_and_clears()
    {
        using var store = CreateDocumentStore();
        using var session = store.LightweightSession(IsolationLevel.ReadCommitted);
        var accessors = CreateAccessors(outbox =>
        {
            outbox.HarvestDomainEvents<CustomOrderDocument>(
                document => document.PendingEvents,
                document => document.ClearPendingEvents());
        });
        var collector = new RecordingCollector();
        var document = new CustomOrderDocument { Id = Guid.NewGuid() };
        var placed = new OrderPlacedEvent { OrderId = document.Id };
        document.PendingEvents.Add(placed);
        session.Delete(document);

        new MartenDomainEventHarvester(session, accessors).HarvestInto(collector);

        collector.Collected.Should().Equal(placed);
        document.PendingEvents.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a predicate deletion harvests nothing, because it carries no document.
    /// </summary>
    [Fact]
    public void DeleteWhere_deletion_without_a_document_harvests_nothing()
    {
        using var store = CreateDocumentStore();
        using var session = store.LightweightSession(IsolationLevel.ReadCommitted);
        var accessors = CreateAccessors();
        var collector = new RecordingCollector();
        var orderId = Guid.NewGuid();
        session.DeleteWhere<OrderDocument>(document => document.Id == orderId);

        session.PendingChanges.Deletions().Should().HaveCount(1);

        new MartenDomainEventHarvester(session, accessors).HarvestInto(collector);

        collector.Collected.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies a null collector is rejected.
    /// </summary>
    [Fact]
    public void Null_collector_is_rejected()
    {
        using var store = CreateDocumentStore();
        using var session = store.LightweightSession(IsolationLevel.ReadCommitted);
        var harvester = new MartenDomainEventHarvester(session, CreateAccessors());

        var act = () => harvester.HarvestInto(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>
    ///     Builds the shared accessor pairs from one outbox composition, with optional accessor configuration.
    /// </summary>
    /// <param name="configure">The accessor configuration applied to the outbox builder, if any.</param>
    /// <returns>The accessor pairs the built composition registered.</returns>
    private static DomainEventAccessors CreateAccessors(Action<OutboxModuleBuilder>? configure = null)
    {
        var provider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ =>
                {
                });

                registry.AddOutbox(outbox =>
                {
                    configure?.Invoke(outbox);
                    outbox.UseInMemoryStorage();
                });
            })
            .BuildServiceProvider();

        return provider.GetRequiredService<DomainEventAccessors>();
    }

    /// <summary>
    ///     Creates a document store whose connection string is never dialled, because tests queue operations only.
    /// </summary>
    /// <returns>The offline document store.</returns>
    private static DocumentStore CreateDocumentStore()
    {
        const string connectionString =
            "Host=localhost;Port=1;Database=marten_domain_event_harvest_tests;Username=postgres;Password=postgres";

        return DocumentStore.For(options =>
        {
            options.Connection(connectionString);
        });
    }

    /// <summary>
    ///     Collects harvested domain events for one test run.
    /// </summary>
    private sealed class RecordingCollector : IDomainEventCollector
    {
        /// <summary>
        ///     The events collected so far.
        /// </summary>
        private readonly List<object> _events = [];

        /// <summary>
        ///     Gets the events collected so far.
        /// </summary>
        public IReadOnlyList<object> Collected => _events;

        /// <summary>
        ///     Records one domain event for later staging.
        /// </summary>
        /// <param name="domainEvent">The domain event to stage when the mediation completes successfully.</param>
        public void Collect(object domainEvent)
        {
            _events.Add(domainEvent);
        }

        /// <summary>
        ///     Removes and returns every collected domain event in collection order.
        /// </summary>
        /// <returns>The collected events, or an empty list when nothing was collected.</returns>
        public IReadOnlyList<object> Drain()
        {
            var drained = _events.ToArray();
            _events.Clear();
            return drained;
        }
    }

    /// <summary>
    ///     A Marten document that buffers its domain events through the shared contract.
    /// </summary>
    private sealed class OrderDocument : IHasDomainEvents
    {
        /// <summary>
        ///     The buffered domain events raised since the last harvest.
        /// </summary>
        private readonly List<object> _events = [];

        /// <summary>
        ///     Gets or sets the document identity.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        ///     Gets the domain events the document raised and has not yet handed over.
        /// </summary>
        public IReadOnlyCollection<object> DomainEvents => _events;

        /// <summary>
        ///     Buffers one domain event for the next harvest.
        /// </summary>
        /// <param name="domainEvent">The domain event to buffer.</param>
        public void Raise(object domainEvent)
        {
            _events.Add(domainEvent);
        }

        /// <summary>
        ///     Removes every buffered domain event, after a harvester collected them.
        /// </summary>
        public void ClearDomainEvents()
        {
            _events.Clear();
        }
    }

    /// <summary>
    ///     A Marten document that buffers its domain events in its own list, read through configured accessors.
    /// </summary>
    private sealed class CustomOrderDocument
    {
        /// <summary>
        ///     Gets or sets the document identity.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        ///     Gets the buffered domain events raised since the last harvest.
        /// </summary>
        public List<object> PendingEvents { get; } = [];

        /// <summary>
        ///     Removes every buffered domain event, after a harvester collected them.
        /// </summary>
        public void ClearPendingEvents()
        {
            PendingEvents.Clear();
        }
    }

    /// <summary>
    ///     The domain event harvested from order documents.
    /// </summary>
    private sealed record OrderPlacedEvent
    {
        /// <summary>
        ///     Gets the placed order identifier.
        /// </summary>
        public required Guid OrderId { get; init; }
    }
}
