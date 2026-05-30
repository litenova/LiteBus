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
    public async Task OutboxWriter_ShouldStoreEventWithExplicitMessageId()
    {
        var now = new DateTimeOffset(2026, 5, 28, 11, 0, 0, TimeSpan.Zero);
        var store = new InMemoryOutboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 3);

        var outbox = new OutboxWriter(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new FixedTimeProvider(now));

        var eventId = Guid.NewGuid();

        var receipt = await outbox.AddAsync(new OrderSubmittedIntegrationEvent
        {
            OrderId = Guid.NewGuid()
        }, new OutboxOptions
        {
            Id = eventId,
            Topic = "orders",
            CorrelationId = "correlation-1"
        });

        receipt.Id.Should().Be(eventId);
        receipt.MessageType.Should().Be(typeof(OrderSubmittedIntegrationEvent));
        receipt.ContractName.Should().Be("orders.events.submitted");
        receipt.ContractVersion.Should().Be(3);
        receipt.StoredAt.Should().Be(now);

        var envelope = store.Get(eventId);
        envelope.Topic.Should().Be("orders");
        envelope.Status.Should().Be(OutboxStatus.Pending);
        envelope.CorrelationId.Should().Be("correlation-1");
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldDispatchThroughMockDispatcherAndMarkPublished()
    {
        var store = new InMemoryOutboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IOutboxStore>(store)
            .AddSingleton<IOutboxLeaseStore>(store)
            .AddSingleton<IOutboxStateStore>(store)
            .AddSingleton<OutboxTestInfrastructure.RecordingOutboxDispatcher>()
            .AddSingleton<IOutboxDispatcher>(sp => sp.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>())
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
                            UseJitter = false
                        }
                    });
                });
            })
            .BuildServiceProvider();

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = serviceProvider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>();
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent
        {
            OrderId = orderId
        }, new OutboxOptions
        {
            Id = eventId
        });

        await processor.ProcessPendingAsync();

        dispatcher.DispatchedMessages
            .OfType<OrderSubmittedIntegrationEvent>()
            .Should()
            .ContainSingle(submitted => submitted.OrderId == orderId);

        var envelope = store.Get(eventId);
        envelope.Status.Should().Be(OutboxStatus.Published);
        envelope.AttemptCount.Should().Be(1);
        envelope.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldSupportClosedGenericIntegrationEvents()
    {
        var store = new InMemoryOutboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IOutboxStore>(store)
            .AddSingleton<IOutboxLeaseStore>(store)
            .AddSingleton<IOutboxStateStore>(store)
            .AddSingleton<OutboxTestInfrastructure.RecordingOutboxDispatcher>()
            .AddSingleton<IOutboxDispatcher>(sp => sp.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>())
            .AddLiteBus(configuration =>
            {
                configuration.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<GenericIntegrationEvent<int>>("generic.events.int", 1);
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

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = serviceProvider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new GenericIntegrationEvent<int>
        {
            Value = 42
        }, new OutboxOptions
        {
            Id = messageId
        });

        await processor.ProcessPendingAsync();

        dispatcher.DispatchedMessages
            .OfType<GenericIntegrationEvent<int>>()
            .Should()
            .ContainSingle(generic => generic.Value == 42);

        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);
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
            .AddSingleton<IOutboxStore>(store)
            .AddSingleton<IOutboxLeaseStore>(store)
            .AddSingleton<IOutboxStateStore>(store)
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

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent
        {
            OrderId = Guid.NewGuid()
        }, new OutboxOptions { Id = messageId });

        await processor.ProcessPendingAsync();

        var envelope = store.Get(messageId);
        envelope.Status.Should().Be(OutboxStatus.Failed);
        envelope.LastError.Should().NotBeNullOrWhiteSpace();
        envelope.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherExceedsMaxAttempts_ShouldMoveToDeadLetter()
    {
        var store = new InMemoryOutboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IOutboxStore>(store)
            .AddSingleton<IOutboxLeaseStore>(store)
            .AddSingleton<IOutboxStateStore>(store)
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

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent
        {
            OrderId = Guid.NewGuid()
        }, new OutboxOptions { Id = messageId });

        // Attempt 1 of 2: AttemptCount reaches 1 which is < MaxAttempts (2), so envelope is retried.
        await processor.ProcessPendingAsync();
        // Attempt 2 of 2: AttemptCount reaches 2 which is >= MaxAttempts (2), so envelope is dead-lettered.
        await processor.ProcessPendingAsync();

        store.Get(messageId).Status.Should().Be(OutboxStatus.DeadLettered);
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
            Id = messageId,
            VisibleAfter = visibleAfter
        });

        store.Get(messageId).VisibleAfter.Should().Be(visibleAfter);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldPassTraceMetadataToDispatcher()
    {
        var store = new InMemoryOutboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IOutboxStore>(store)
            .AddSingleton<IOutboxLeaseStore>(store)
            .AddSingleton<IOutboxStateStore>(store)
            .AddSingleton<OutboxTestInfrastructure.RecordingOutboxDispatcher>()
            .AddSingleton<IOutboxDispatcher>(sp => sp.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>())
            .AddLiteBus(configuration =>
            {
                configuration.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });
            })
            .BuildServiceProvider();

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = serviceProvider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            Id = messageId,
            CorrelationId = "correlation-99",
            CausationId = "causation-99",
            TenantId = "tenant-99"
        });

        await processor.ProcessPendingAsync();

        var envelope = dispatcher.DispatchedEnvelopes.Should().ContainSingle().Subject;
        envelope.CorrelationId.Should().Be("correlation-99");
        envelope.CausationId.Should().Be("causation-99");
        envelope.TenantId.Should().Be("tenant-99");
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

    public sealed record OrderSubmittedIntegrationEvent
    {
        public Guid OrderId { get; init; }
    }

    public sealed record GenericIntegrationEvent<T>
    {
        public required T Value { get; init; }
    }

    public sealed class AlwaysFailingOutboxDispatcher : IOutboxDispatcher
    {
        public Task DispatchAsync(OutboxEnvelope message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated dispatcher failure.");
        }
    }

    public sealed class InMemoryOutboxStore : IOutboxStore, IOutboxLeaseStore, IOutboxStateStore
    {
        private readonly Dictionary<Guid, OutboxEnvelope> _envelopes = [];

        public Task<OutboxEnvelope> AddAsync(OutboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (_envelopes.TryGetValue(envelope.Id, out var existing))
            {
                return Task.FromResult(existing);
            }

            _envelopes[envelope.Id] = envelope;
            return Task.FromResult(envelope);
        }

        public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(OutboxLeaseRequest request, CancellationToken cancellationToken = default)
        {
            var leased = _envelopes.Values
                .Where(envelope => IsAvailable(envelope, request.Now))
                .OrderBy(envelope => envelope.CreatedAt)
                .Take(request.BatchSize)
                .Select(envelope => envelope with
                {
                    Status = OutboxStatus.Publishing,
                    LeaseOwner = request.LeaseOwner,
                    LeaseExpiresAt = request.Now.Add(request.LeaseDuration),
                    AttemptCount = envelope.AttemptCount + 1
                })
                .ToArray();

            foreach (var envelope in leased)
            {
                _envelopes[envelope.Id] = envelope;
            }

            return Task.FromResult<IReadOnlyList<OutboxEnvelope>>(leased);
        }

        public Task MarkPublishedAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            var envelope = Get(messageId);
            _envelopes[messageId] = envelope with
            {
                Status = OutboxStatus.Published,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = null
            };

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(OutboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
        {
            var envelope = Get(failure.Id);
            _envelopes[failure.Id] = envelope with
            {
                Status = OutboxStatus.Failed,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = failure.Error,
                VisibleAfter = failure.VisibleAfter
            };

            return Task.CompletedTask;
        }

        public Task MoveToDeadLetterAsync(OutboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
        {
            var envelope = Get(deadLetter.Id);
            _envelopes[deadLetter.Id] = envelope with
            {
                Status = OutboxStatus.DeadLettered,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = deadLetter.Reason
            };

            return Task.CompletedTask;
        }

        public OutboxEnvelope Get(Guid messageId)
        {
            return _envelopes[messageId];
        }

        public IReadOnlyList<OutboxEnvelope> GetAll()
        {
            return _envelopes.Values.ToList();
        }

        private static bool IsAvailable(OutboxEnvelope envelope, DateTimeOffset now)
        {
            return ((envelope.Status is OutboxStatus.Pending or OutboxStatus.Failed) &&
                    (envelope.VisibleAfter is null || envelope.VisibleAfter <= now)) ||
                   (envelope.Status == OutboxStatus.Publishing && envelope.LeaseExpiresAt <= now);
        }
    }
}
