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
public sealed class OutboxProcessorEdgeCaseTests : LiteBusTestBase
{
    private static readonly DateTimeOffset BaseTime = new(2026, 5, 29, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AddAsync_WhenMessageIdSupplied_ShouldUseProvidedId()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);

        var writer = new OutboxWriter(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var messageId = Guid.NewGuid();
        var receipt = await writer.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            MessageId = messageId
        });

        receipt.MessageId.Should().Be(messageId);
        store.Get(messageId).MessageId.Should().Be(messageId);
    }

    [Fact]
    public async Task AddAsync_WhenMessageIdOmitted_ShouldGenerateId()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);

        var writer = new OutboxWriter(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var receipt = await writer.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() });

        receipt.MessageId.Should().NotBe(Guid.Empty);
        store.Get(receipt.MessageId).MessageId.Should().Be(receipt.MessageId);
    }

    [Fact]
    public async Task AddAsync_WhenDuplicateMessageId_ShouldReturnExistingEnvelope()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);

        var writer = new OutboxWriter(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var messageId = Guid.NewGuid();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();

        await writer.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = firstOrderId }, new OutboxOptions { MessageId = messageId });
        await writer.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = secondOrderId }, new OutboxOptions { MessageId = messageId });

        store.GetAll().Should().HaveCount(1);
        store.Get(messageId).Topic.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_WhenContractNotRegistered_ShouldThrowMessageContractNotRegisteredException()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var writer = new OutboxWriter(
            store,
            new MessageContractRegistry(),
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var act = async () => await writer.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() });

        await act.Should().ThrowAsync<MessageContractNotRegisteredException>();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldProcessMultipleMessagesInSinglePass()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var recorder = new OutboxTests.EventRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10);

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        for (var i = 0; i < 3; i++)
        {
            await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
            {
                MessageId = Guid.NewGuid()
            });
        }

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(3);
        recorder.Events.Should().HaveCount(3);
        store.GetAll().Should().OnlyContain(envelope => envelope.Status == OutboxMessageStatus.Published);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldRespectBatchSize()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var recorder = new OutboxTests.EventRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 2);

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        for (var i = 0; i < 5; i++)
        {
            await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
            {
                MessageId = Guid.NewGuid()
            });
        }

        var firstPass = await processor.ProcessPendingAsync();
        firstPass.LeasedCount.Should().Be(2);
        recorder.Events.Should().HaveCount(2);

        var secondPass = await processor.ProcessPendingAsync();
        secondPass.LeasedCount.Should().Be(2);
        recorder.Events.Should().HaveCount(4);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseMessage()
    {
        var clock = new OutboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new OutboxTests.InMemoryOutboxStore();
        var recorder = new OutboxTests.EventRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10, clock: clock);

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            MessageId = messageId,
            VisibleAfter = BaseTime.AddHours(1)
        });

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(0);
        recorder.Events.Should().BeEmpty();
        store.Get(messageId).Status.Should().Be(OutboxMessageStatus.Pending);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenVisibleAfterReached_ShouldPublishMessage()
    {
        var clock = new OutboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new OutboxTests.InMemoryOutboxStore();
        var recorder = new OutboxTests.EventRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10, clock: clock);

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            MessageId = messageId,
            VisibleAfter = BaseTime.AddMinutes(10)
        });

        clock.Advance(TimeSpan.FromMinutes(10));

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(1);
        recorder.Events.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenFixedBackoffConfigured_ShouldSetVisibleAfterToInitialDelay()
    {
        var clock = new OutboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new OutboxTests.InMemoryOutboxStore();
        await using var provider = BuildProcessorProvider(
            store,
            recorder: null,
            batchSize: 10,
            clock: clock,
            useFailingDispatcher: true,
            configureOutbox: outbox =>
            {
                outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                outbox.UseProcessorOptions(new OutboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "test-publisher",
                    Retry = new RetryOptions
                    {
                        MaxAttempts = 5,
                        InitialDelay = TimeSpan.FromMinutes(2),
                        Backoff = RetryBackoff.Fixed,
                        UseJitter = false
                    }
                });
            });

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { MessageId = messageId });
        await processor.ProcessPendingAsync();

        store.Get(messageId).VisibleAfter.Should().Be(BaseTime.AddMinutes(2));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimStuckMessage()
    {
        var clock = new OutboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new OutboxTests.InMemoryOutboxStore();
        var recorder = new OutboxTests.EventRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10, clock: clock);

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { MessageId = messageId });

        await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-publisher",
            Now = BaseTime,
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        store.Get(messageId).Status.Should().Be(OutboxMessageStatus.Publishing);

        clock.Advance(TimeSpan.FromMinutes(1));

        await processor.ProcessPendingAsync();

        recorder.Events.Should().ContainSingle();
        store.Get(messageId).Status.Should().Be(OutboxMessageStatus.Published);
        store.Get(messageId).AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherThrows_ShouldStoreErrorWithoutStackTrace()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        await using var provider = BuildProcessorProvider(
            store,
            recorder: null,
            batchSize: 10,
            useFailingDispatcher: true);

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { MessageId = messageId });
        await processor.ProcessPendingAsync();

        var lastError = store.Get(messageId).LastError;
        lastError.Should().Be($"{typeof(InvalidOperationException).FullName}: Simulated dispatcher failure.");
        lastError.Should().NotContain(" at ");
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldReturnLeasedCount()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var recorder = new OutboxTests.EventRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10);

        var outbox = provider.GetRequiredService<IIntegrationOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        var emptyPass = await processor.ProcessPendingAsync();
        emptyPass.LeasedCount.Should().Be(0);

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { MessageId = Guid.NewGuid() });

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(1);
    }

    [Fact]
    public async Task LiteBusEventOutboxDispatcher_ShouldPublishPocoEvent()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        var recorder = new PocoEventRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IOutboxMessageWriter>(store)
            .AddSingleton<IOutboxMessageLeaseStore>(store)
            .AddSingleton<IOutboxMessageStateStore>(store)
            .AddSingleton(recorder)
            .AddLiteBus(configuration =>
            {
                configuration.AddEventModule(builder => builder.Register<PocoEventHandler>());
                configuration.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<PocoIntegrationEvent>("poco.events.sample", 1);
                    builder.UseLiteBusEventDispatcher();
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "poco-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });
            })
            .BuildServiceProvider();

        var writer = serviceProvider.GetRequiredService<IOutboxWriter>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await writer.AddAsync(new PocoIntegrationEvent { Value = "poco-test" }, new OutboxOptions { MessageId = messageId });
        await processor.ProcessPendingAsync();

        recorder.Values.Should().ContainSingle("poco-test");
    }

    [Fact]
    public void OutboxProcessor_WithInvalidBatchSize_ShouldThrow()
    {
        var act = () => new OutboxProcessor(
            new OutboxTests.InMemoryOutboxStore(),
            new OutboxTests.InMemoryOutboxStore(),
            new OutboxTests.AlwaysFailingOutboxDispatcher(),
            new OutboxProcessorOptions { BatchSize = 0 },
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OutboxProcessor_WithInvalidLeaseDuration_ShouldThrow()
    {
        var act = () => new OutboxProcessor(
            new OutboxTests.InMemoryOutboxStore(),
            new OutboxTests.InMemoryOutboxStore(),
            new OutboxTests.AlwaysFailingOutboxDispatcher(),
            new OutboxProcessorOptions { LeaseDuration = TimeSpan.Zero },
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OutboxProcessor_WithInvalidMaxAttempts_ShouldThrow()
    {
        var act = () => new OutboxProcessor(
            new OutboxTests.InMemoryOutboxStore(),
            new OutboxTests.InMemoryOutboxStore(),
            new OutboxTests.AlwaysFailingOutboxDispatcher(),
            new OutboxProcessorOptions { Retry = new RetryOptions { MaxAttempts = 0 } },
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    public sealed record PocoIntegrationEvent
    {
        public required string Value { get; init; }
    }

    public sealed class PocoEventHandler : IEventHandler<PocoIntegrationEvent>
    {
        private readonly PocoEventRecorder _recorder;

        public PocoEventHandler(PocoEventRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(PocoIntegrationEvent message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message.Value);
            return Task.CompletedTask;
        }
    }

    public sealed class PocoEventRecorder
    {
        private readonly List<string> _values = [];

        public IReadOnlyList<string> Values => _values;

        public void Record(string value)
        {
            _values.Add(value);
        }
    }

    private static ServiceProvider BuildProcessorProvider(
        OutboxTests.InMemoryOutboxStore store,
        OutboxTests.EventRecorder? recorder,
        int batchSize,
        TimeProvider? clock = null,
        bool useFailingDispatcher = false,
        Action<OutboxModuleBuilder>? configureOutbox = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IOutboxMessageWriter>(store)
            .AddSingleton<IOutboxMessageLeaseStore>(store)
            .AddSingleton<IOutboxMessageStateStore>(store);

        if (recorder is not null)
        {
            services.AddSingleton(recorder);
        }

        if (useFailingDispatcher)
        {
            services.AddSingleton<IOutboxDispatcher>(new OutboxTests.AlwaysFailingOutboxDispatcher());
        }

        services.AddLiteBus(configuration =>
        {
            if (recorder is not null)
            {
                configuration.AddEventModule(builder => builder.Register<OutboxTests.OrderSubmittedEventHandler>());
            }

            configuration.AddOutboxModule(outbox =>
            {
                if (configureOutbox is not null)
                {
                    configureOutbox(outbox);
                }
                else
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
                    if (!useFailingDispatcher)
                    {
                        outbox.UseLiteBusEventDispatcher();
                    }

                    outbox.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = batchSize,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                }
            });
        });

        if (clock is not null)
        {
            services.AddSingleton(clock);
        }

        return services.BuildServiceProvider();
    }
}
