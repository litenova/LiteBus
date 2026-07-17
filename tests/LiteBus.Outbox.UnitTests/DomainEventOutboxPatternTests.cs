using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions.DurableMessaging;
using LiteBus.Outbox.Abstractions;

namespace LiteBus.Outbox.UnitTests;

/// <summary>
///     Verifies the application-owned domain-event collector and transactional outbox mapping pattern.
/// </summary>
public sealed class DomainEventOutboxPatternTests
{
    /// <summary>
    ///     Confirms a handler drains aggregate events, maps them, and stages integration events in the bound outbox store.
    /// </summary>
    [Fact]
    public async Task HandleAsync_WithRecordedDomainEvent_ShouldDrainMapAndEnqueueIntegrationEvent()
    {
        var store = new RecordingTransactionalOutboxStore();
        var registry = new MessageContractRegistry();
        registry.Register<OrderPlacedIntegrationEvent>("orders.events.placed");
        var serializer = new SystemTextJsonMessageSerializer();
        var transactionalOutbox = new StoreBoundTransactionalOutbox(
            store,
            new OutboxEnvelopeFactory(registry, serializer, TimeProvider.System));
        var handler = new PlaceOrderHandler(
            new DomainEventCollector(),
            new OrderIntegrationEventMapper(),
            transactionalOutbox);
        var order = new Order(Guid.NewGuid());

        await handler.HandleAsync(order).ConfigureAwait(false);

        order.DomainEvents.Should().BeEmpty();
        store.Envelopes.Should().ContainSingle();
        var envelope = store.Envelopes[0];
        envelope.ContractName.Should().Be("orders.events.placed");
        envelope.Topic.Should().Be("orders.events");
        envelope.IdempotencyKey.Should().Be($"order-placed:{order.Id:N}");

        var integrationEvent = (OrderPlacedIntegrationEvent)await serializer.DeserializeAsync(
            typeof(OrderPlacedIntegrationEvent),
            envelope.Payload).ConfigureAwait(false);
        integrationEvent.OrderId.Should().Be(order.Id);
    }

    private interface IDomainEvent;

    private interface IHasDomainEvents
    {
        IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

        IReadOnlyList<IDomainEvent> DrainDomainEvents();
    }

    private abstract class AggregateRoot : IHasDomainEvents
    {
        private readonly List<IDomainEvent> _domainEvents = [];

        public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

        public IReadOnlyList<IDomainEvent> DrainDomainEvents()
        {
            IDomainEvent[] events = [.. _domainEvents];
            _domainEvents.Clear();
            return events;
        }

        protected void AddDomainEvent(IDomainEvent domainEvent)
        {
            ArgumentNullException.ThrowIfNull(domainEvent);
            _domainEvents.Add(domainEvent);
        }
    }

    private sealed class Order : AggregateRoot
    {
        public Order(Guid id)
        {
            Id = id;
        }

        public Guid Id { get; }

        public void Place()
        {
            AddDomainEvent(new OrderPlacedDomainEvent(Id));
        }
    }

    private sealed record OrderPlacedDomainEvent(Guid OrderId) : IDomainEvent;

    private sealed record OrderPlacedIntegrationEvent
    {
        public Guid OrderId { get; init; }
    }

    private sealed class DomainEventCollector
    {
        public IReadOnlyList<IDomainEvent> Drain(IHasDomainEvents aggregate)
        {
            ArgumentNullException.ThrowIfNull(aggregate);
            return aggregate.DrainDomainEvents();
        }
    }

    private sealed class OrderIntegrationEventMapper
    {
        public OutboxEnqueueItem<OrderPlacedIntegrationEvent> Map(IDomainEvent domainEvent)
        {
            ArgumentNullException.ThrowIfNull(domainEvent);

            return domainEvent switch
            {
                OrderPlacedDomainEvent placed => OutboxEnqueueItem<OrderPlacedIntegrationEvent>.From(
                    new OrderPlacedIntegrationEvent { OrderId = placed.OrderId },
                    OutboxEnqueueMetadata.Immediate with
                    {
                        Idempotency = new Idempotency.Keyed($"order-placed:{placed.OrderId:N}"),
                        Target = new PublicationTarget.Topic("orders.events")
                    }),
                _ => throw new ArgumentOutOfRangeException(nameof(domainEvent), domainEvent, "Unsupported domain event type.")
            };
        }
    }

    private sealed class PlaceOrderHandler
    {
        private readonly DomainEventCollector _collector;
        private readonly OrderIntegrationEventMapper _mapper;
        private readonly ITransactionalOutbox _transactionalOutbox;

        public PlaceOrderHandler(
            DomainEventCollector collector,
            OrderIntegrationEventMapper mapper,
            ITransactionalOutbox transactionalOutbox)
        {
            ArgumentNullException.ThrowIfNull(collector);
            ArgumentNullException.ThrowIfNull(mapper);
            ArgumentNullException.ThrowIfNull(transactionalOutbox);

            _collector = collector;
            _mapper = mapper;
            _transactionalOutbox = transactionalOutbox;
        }

        public async Task HandleAsync(Order order, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(order);

            order.Place();

            foreach (var domainEvent in _collector.Drain(order))
            {
                await _transactionalOutbox.EnqueueAsync(_mapper.Map(domainEvent), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private sealed class RecordingTransactionalOutboxStore : ITransactionalOutboxStore
    {
        private readonly List<OutboxEnvelope> _envelopes = [];

        public IReadOnlyList<OutboxEnvelope> Envelopes => _envelopes;

        public Task<OutboxAppendResult> AddAsync(
            OutboxEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            _envelopes.Add(envelope);
            return Task.FromResult(new OutboxAppendResult(envelope, OutboxEnqueueOutcome.Enqueued));
        }

        public Task<IReadOnlyList<OutboxAppendResult>> AddBatchAsync(
            IReadOnlyList<OutboxEnvelope> envelopes,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(envelopes);
            _envelopes.AddRange(envelopes);
            return Task.FromResult<IReadOnlyList<OutboxAppendResult>>(
                envelopes
                    .Select(envelope => new OutboxAppendResult(envelope, OutboxEnqueueOutcome.Enqueued))
                    .ToArray());
        }
    }
}
