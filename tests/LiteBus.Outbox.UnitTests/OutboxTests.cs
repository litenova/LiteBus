using LiteBus.Events;
using LiteBus.Events.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Outbox.UnitTests;

[Collection("Sequential")]
public sealed class OutboxTests : LiteBusTestBase
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
                        Retry = new RetryOptions
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
                        Retry = new RetryOptions
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

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherThrows_ShouldMarkFailedAndSetVisibleAfter()
    {
        var store = new InMemoryOutboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IOutboxMessageWriter>(store)
            .AddSingleton<IOutboxMessageLeaseStore>(store)
            .AddSingleton<IOutboxMessageStateStore>(store)
            .AddSingleton<IOutboxDispatcher>(new AlwaysFailingOutboxDispatcher())
            .AddLiteBus(configuration =>
            {
                configuration.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions
                        {
                            MaxAttempts = 3,
                            InitialDelay = TimeSpan.Zero,
                            UseJitter = false
                        }
                    });
                });
            })
            .BuildServiceProvider();

        var outbox = serviceProvider.GetRequiredService<IIntegrationOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent
        {
            OrderId = Guid.NewGuid()
        }, new OutboxOptions { MessageId = messageId });

        await processor.ProcessPendingAsync();

        var envelope = store.Get(messageId);
        envelope.Status.Should().Be(OutboxMessageStatus.Failed);
        envelope.LastError.Should().NotBeNullOrWhiteSpace();
        envelope.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherExceedsMaxAttempts_ShouldMoveToDeadLetter()
    {
        var store = new InMemoryOutboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IOutboxMessageWriter>(store)
            .AddSingleton<IOutboxMessageLeaseStore>(store)
            .AddSingleton<IOutboxMessageStateStore>(store)
            .AddSingleton<IOutboxDispatcher>(new AlwaysFailingOutboxDispatcher())
            .AddLiteBus(configuration =>
            {
                configuration.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions
                        {
                            MaxAttempts = 2,
                            InitialDelay = TimeSpan.Zero,
                            UseJitter = false
                        }
                    });
                });
            })
            .BuildServiceProvider();

        var outbox = serviceProvider.GetRequiredService<IIntegrationOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent
        {
            OrderId = Guid.NewGuid()
        }, new OutboxOptions { MessageId = messageId });

        // Attempt 1 of 2: AttemptCount reaches 1 which is < MaxAttempts (2), so envelope is retried.
        await processor.ProcessPendingAsync();
        // Attempt 2 of 2: AttemptCount reaches 2 which is >= MaxAttempts (2), so envelope is dead-lettered.
        await processor.ProcessPendingAsync();

        store.Get(messageId).Status.Should().Be(OutboxMessageStatus.DeadLettered);
    }

    [Fact]
    public async Task AddAsync_ShouldPersistVisibleAfterFromOptions()
    {
        var now = new DateTimeOffset(2026, 5, 29, 12, 0, 0, TimeSpan.Zero);
        var visibleAfter = now.AddHours(2);
        var store = new InMemoryOutboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);

        var writer = new OutboxWriter(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new FixedTimeProvider(now));

        var messageId = Guid.NewGuid();

        await writer.AddAsync(new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            MessageId = messageId,
            VisibleAfter = visibleAfter
        });

        store.Get(messageId).VisibleAfter.Should().Be(visibleAfter);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldPropagateTraceMetadataToEventHandlers()
    {
        var store = new InMemoryOutboxStore();
        var capture = new TraceMetadataCapture();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IOutboxMessageWriter>(store)
            .AddSingleton<IOutboxMessageLeaseStore>(store)
            .AddSingleton<IOutboxMessageStateStore>(store)
            .AddSingleton(capture)
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder =>
                {
                    builder.Register<TraceMetadataEventHandler>();
                });

                configuration.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    builder.UseLiteBusEventDispatcher();
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });
            })
            .BuildServiceProvider();

        var outbox = serviceProvider.GetRequiredService<IIntegrationOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            MessageId = messageId,
            CorrelationId = "correlation-99",
            CausationId = "causation-99",
            TenantId = "tenant-99"
        });

        await processor.ProcessPendingAsync();

        capture.CorrelationId.Should().Be("correlation-99");
        capture.CausationId.Should().Be("causation-99");
        capture.TenantId.Should().Be("tenant-99");
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

    public sealed class TraceMetadataCapture
    {
        public string? CorrelationId { get; set; }

        public string? CausationId { get; set; }

        public string? TenantId { get; set; }
    }

    public sealed class TraceMetadataEventHandler : IEventHandler<OrderSubmittedIntegrationEvent>
    {
        private readonly TraceMetadataCapture _capture;

        public TraceMetadataEventHandler(TraceMetadataCapture capture)
        {
            _capture = capture;
        }

        public Task HandleAsync(OrderSubmittedIntegrationEvent message, CancellationToken cancellationToken = default)
        {
            var items = AmbientExecutionContext.Current.Items;
            _capture.CorrelationId = items.TryGetValue(MessageTraceContextKeys.CorrelationId, out var correlation)
                ? correlation as string
                : null;
            _capture.CausationId = items.TryGetValue(MessageTraceContextKeys.CausationId, out var causation)
                ? causation as string
                : null;
            _capture.TenantId = items.TryGetValue(MessageTraceContextKeys.TenantId, out var tenant)
                ? tenant as string
                : null;

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

    public sealed class AlwaysFailingOutboxDispatcher : IOutboxDispatcher
    {
        public Task DispatchAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated dispatcher failure.");
        }
    }

    public sealed class InMemoryOutboxStore : IOutboxMessageWriter, IOutboxMessageLeaseStore, IOutboxMessageStateStore
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

        public IReadOnlyList<OutboxMessageEnvelope> GetAll()
        {
            return _envelopes.Values.ToList();
        }

        private static bool IsAvailable(OutboxMessageEnvelope envelope, DateTimeOffset now)
        {
            return ((envelope.Status is OutboxMessageStatus.Pending or OutboxMessageStatus.Failed) &&
                    (envelope.VisibleAfter is null || envelope.VisibleAfter <= now)) ||
                   (envelope.Status == OutboxMessageStatus.Publishing && envelope.LeaseExpiresAt <= now);
        }
    }
}
