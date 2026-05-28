using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests;

[Collection("Sequential")]
public sealed class OutboxTests
{
    [Fact]
    public async Task IntegrationOutbox_ShouldStoreEventWithExplicitMessageId()
    {
        var now = new DateTimeOffset(2026, 5, 28, 11, 0, 0, TimeSpan.Zero);
        var store = new InMemoryOutboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 3);

        var outbox = new IntegrationOutbox(new OutboxWriter(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new FixedTimeProvider(now)));

        var eventId = Guid.NewGuid();

        var receipt = await outbox.AddAsync(new OrderSubmittedIntegrationEvent
        {
            OrderId = Guid.NewGuid()
        }, new OutboxOptions
        {
            MessageId = eventId,
            Topic = "orders",
            CorrelationId = "correlation-1"
        });

        receipt.MessageId.Should().Be(eventId);
        receipt.EventType.Should().Be(typeof(OrderSubmittedIntegrationEvent));
        receipt.ContractName.Should().Be("orders.events.submitted");
        receipt.ContractVersion.Should().Be(3);
        receipt.StoredAt.Should().Be(now);

        var envelope = store.Get(eventId);
        envelope.Topic.Should().Be("orders");
        envelope.Status.Should().Be(OutboxMessageStatus.Pending);
        envelope.CorrelationId.Should().Be("correlation-1");
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldPublishThroughLiteBusEventDispatcherAndMarkPublished()
    {
        var store = new InMemoryOutboxStore();
        var recorder = new EventRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IOutboxMessageWriter>(store)
            .AddSingleton<IOutboxMessageLeaseStore>(store)
            .AddSingleton<IOutboxMessageStateStore>(store)
            .AddSingleton(recorder)
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.Register<OrderSubmittedEventHandler>();
                });

                configuration.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    builder.UseLiteBusEventDispatcher();
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new DurableRetryOptions
                        {
                            UseJitter = false
                        }
                    });
                });
            })
            .BuildServiceProvider();

        var outbox = serviceProvider.GetRequiredService<IIntegrationOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent
        {
            OrderId = orderId
        }, new OutboxOptions
        {
            MessageId = eventId
        });

        await processor.ProcessPendingAsync();

        recorder.Events.Should().ContainSingle(@event => @event.OrderId == orderId);

        var envelope = store.Get(eventId);
        envelope.Status.Should().Be(OutboxMessageStatus.Published);
        envelope.AttemptCount.Should().Be(1);
        envelope.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldSupportClosedGenericIntegrationEvents()
    {
        var store = new InMemoryOutboxStore();
        var recorder = new GenericEventRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IOutboxMessageWriter>(store)
            .AddSingleton<IOutboxMessageLeaseStore>(store)
            .AddSingleton<IOutboxMessageStateStore>(store)
            .AddSingleton(recorder)
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.Register<GenericIntegrationEventHandler>();
                });

                configuration.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<GenericIntegrationEvent<int>>("generic.events.int", 1);
                    builder.UseLiteBusEventDispatcher();
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "generic-test-publisher",
                        Retry = new DurableRetryOptions
                        {
                            UseJitter = false
                        }
                    });
                });
            })
            .BuildServiceProvider();

        var outbox = serviceProvider.GetRequiredService<IIntegrationOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new GenericIntegrationEvent<int>
        {
            Value = 42
        }, new OutboxOptions
        {
            MessageId = messageId
        });

        await processor.ProcessPendingAsync();

        recorder.Values.Should().ContainSingle(value => value == 42);
        store.Get(messageId).Status.Should().Be(OutboxMessageStatus.Published);
    }

    [Fact]
    public void Register_ShouldRejectOpenGenericDurableContracts()
    {
        var contractRegistry = new MessageContractRegistry();

        var act = () => contractRegistry.Register(typeof(GenericIntegrationEvent<>), "generic.events.open", 1);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*closed message type*");
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public FixedTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }
    }

    public sealed record OrderSubmittedIntegrationEvent : IIntegrationEvent
    {
        public Guid OrderId { get; init; }
    }

    public sealed class OrderSubmittedEventHandler : IEventHandler<OrderSubmittedIntegrationEvent>
    {
        private readonly EventRecorder _recorder;

        public OrderSubmittedEventHandler(EventRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(OrderSubmittedIntegrationEvent message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message);
            return Task.CompletedTask;
        }
    }

    public sealed class EventRecorder
    {
        private readonly List<OrderSubmittedIntegrationEvent> _events = [];

        public IReadOnlyList<OrderSubmittedIntegrationEvent> Events => _events;

        public void Record(OrderSubmittedIntegrationEvent @event)
        {
            _events.Add(@event);
        }
    }

    public sealed record GenericIntegrationEvent<T> : IIntegrationEvent
    {
        public required T Value { get; init; }
    }

    public sealed class GenericIntegrationEventHandler : IEventHandler<GenericIntegrationEvent<int>>
    {
        private readonly GenericEventRecorder _recorder;

        public GenericIntegrationEventHandler(GenericEventRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(GenericIntegrationEvent<int> message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message.Value);
            return Task.CompletedTask;
        }
    }

    public sealed class GenericEventRecorder
    {
        private readonly List<int> _values = [];

        public IReadOnlyList<int> Values => _values;

        public void Record(int value)
        {
            _values.Add(value);
        }
    }

    private sealed class InMemoryOutboxStore : IOutboxMessageWriter, IOutboxMessageLeaseStore, IOutboxMessageStateStore
    {
        private readonly Dictionary<Guid, OutboxMessageEnvelope> _envelopes = [];

        public Task<OutboxMessageEnvelope> AddAsync(OutboxMessageEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (_envelopes.TryGetValue(envelope.MessageId, out var existing))
            {
                return Task.FromResult(existing);
            }

            _envelopes[envelope.MessageId] = envelope;
            return Task.FromResult(envelope);
        }

        public Task<IReadOnlyList<OutboxMessageEnvelope>> LeasePendingAsync(OutboxLeaseRequest request, CancellationToken cancellationToken = default)
        {
            var leased = _envelopes.Values
                .Where(envelope => IsAvailable(envelope, request.Now))
                .OrderBy(envelope => envelope.CreatedAt)
                .Take(request.BatchSize)
                .Select(envelope => envelope with
                {
                    Status = OutboxMessageStatus.Publishing,
                    LeaseOwner = request.LeaseOwner,
                    LeaseExpiresAt = request.Now.Add(request.LeaseDuration),
                    AttemptCount = envelope.AttemptCount + 1
                })
                .ToArray();

            foreach (var envelope in leased)
            {
                _envelopes[envelope.MessageId] = envelope;
            }

            return Task.FromResult<IReadOnlyList<OutboxMessageEnvelope>>(leased);
        }

        public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            var envelope = Get(messageId);
            _envelopes[messageId] = envelope with
            {
                Status = OutboxMessageStatus.Published,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = null
            };

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(OutboxMessageFailure failure, CancellationToken cancellationToken = default)
        {
            var envelope = Get(failure.MessageId);
            _envelopes[failure.MessageId] = envelope with
            {
                Status = OutboxMessageStatus.Failed,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = failure.Error,
                VisibleAfter = failure.VisibleAfter
            };

            return Task.CompletedTask;
        }

        public Task MoveToDeadLetterAsync(OutboxMessageDeadLetter deadLetter, CancellationToken cancellationToken = default)
        {
            var envelope = Get(deadLetter.MessageId);
            _envelopes[deadLetter.MessageId] = envelope with
            {
                Status = OutboxMessageStatus.DeadLettered,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = deadLetter.Reason
            };

            return Task.CompletedTask;
        }

        public OutboxMessageEnvelope Get(Guid messageId)
        {
            return _envelopes[messageId];
        }

        private static bool IsAvailable(OutboxMessageEnvelope envelope, DateTimeOffset now)
        {
            return ((envelope.Status is OutboxMessageStatus.Pending or OutboxMessageStatus.Failed) &&
                    (envelope.VisibleAfter is null || envelope.VisibleAfter <= now)) ||
                   (envelope.Status == OutboxMessageStatus.Publishing && envelope.LeaseExpiresAt <= now);
        }
    }
}