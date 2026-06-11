using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
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

        var outbox = OutboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new ManualTimeProvider(now));

        var eventId = Guid.NewGuid();

        var receipt = await outbox.EnqueueAsync(new OrderSubmittedIntegrationEvent
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
        var dispatcherHolder = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(dispatcherHolder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddOutboxModule(builder =>
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
                    builder.UseInMemoryStorage();
                    builder.UseRecordingOutboxDispatcher(dispatcherHolder);
                });
            })
            .BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = dispatcherHolder.Instance!;
        var eventId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        await outbox.EnqueueAsync(new OrderSubmittedIntegrationEvent
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
        var dispatcherHolder = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(dispatcherHolder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddOutboxModule(builder =>
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
                    builder.UseInMemoryStorage();
                    builder.UseRecordingOutboxDispatcher(dispatcherHolder);
                });
            })
            .BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<InMemoryOutboxStore>();
        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = dispatcherHolder.Instance!;
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(new GenericIntegrationEvent<int>
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
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddOutboxModule(builder =>
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
                    builder.UseInMemoryStorage();
                    builder.UseFixedOutboxDispatcher(new AlwaysFailingOutboxDispatcher());
                });
            })
            .BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(new OrderSubmittedIntegrationEvent
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
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddOutboxModule(builder =>
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
                    builder.UseInMemoryStorage();
                    builder.UseFixedOutboxDispatcher(new AlwaysFailingOutboxDispatcher());
                });
            })
            .BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();

        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(new OrderSubmittedIntegrationEvent
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

        var writer = OutboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new ManualTimeProvider(now));

        var messageId = Guid.NewGuid();

        await writer.EnqueueAsync(new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            Id = messageId,
            VisibleAfter = visibleAfter
        });

        store.Get(messageId).VisibleAfter.Should().Be(visibleAfter);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldPassTraceMetadataToDispatcher()
    {
        var dispatcherHolder = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(dispatcherHolder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ => { });
                registry.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                    builder.UseInMemoryStorage();
                    builder.UseRecordingOutboxDispatcher(dispatcherHolder);
                });
            })
            .BuildServiceProvider();

        var outbox = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = dispatcherHolder.Instance!;
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
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

    [Fact]
    public async Task EnqueueBatchAsync_ShouldStoreAllEventsWithRuntimeTypes()
    {
        var store = new InMemoryOutboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
        contractRegistry.Register<GenericIntegrationEvent<string>>("orders.events.generic", 1);

        var outbox = OutboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var receipts = await outbox.EnqueueBatchAsync(
            [
                new OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                new GenericIntegrationEvent<string> { Value = "batch" }
            ],
            [typeof(OrderSubmittedIntegrationEvent), typeof(GenericIntegrationEvent<string>)]);

        receipts.Should().HaveCount(2);
        receipts[0].ContractName.Should().Be("orders.events.submitted");
        receipts[1].ContractName.Should().Be("orders.events.generic");
        store.GetAll().Should().HaveCount(2);
    }

    public abstract record BaseIntegrationEvent;

    public sealed record OrderSubmittedIntegrationEvent : BaseIntegrationEvent
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

}
