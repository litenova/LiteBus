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
            Id = messageId
        });

        receipt.Id.Should().Be(messageId);
        store.Get(messageId).Id.Should().Be(messageId);
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

        receipt.Id.Should().NotBe(Guid.Empty);
        store.Get(receipt.Id).Id.Should().Be(receipt.Id);
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

        await writer.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = firstOrderId }, new OutboxOptions { Id = messageId });
        await writer.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = secondOrderId }, new OutboxOptions { Id = messageId });

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
        await using var provider = BuildProcessorProvider(store, batchSize: 10);

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = provider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>();

        for (var i = 0; i < 3; i++)
        {
            await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
            {
                Id = Guid.NewGuid()
            });
        }

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(3);
        dispatcher.DispatchedMessages.Should().HaveCount(3);
        store.GetAll().Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Published);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldRespectBatchSize()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        await using var provider = BuildProcessorProvider(store, batchSize: 2);

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = provider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>();

        for (var i = 0; i < 5; i++)
        {
            await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
            {
                Id = Guid.NewGuid()
            });
        }

        var firstPass = await processor.ProcessPendingAsync();
        firstPass.LeasedCount.Should().Be(2);
        dispatcher.DispatchedMessages.Should().HaveCount(2);

        var secondPass = await processor.ProcessPendingAsync();
        secondPass.LeasedCount.Should().Be(2);
        dispatcher.DispatchedMessages.Should().HaveCount(4);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseMessage()
    {
        var clock = new OutboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new OutboxTests.InMemoryOutboxStore();
        await using var provider = BuildProcessorProvider(store, batchSize: 10, clock: clock);

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = provider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            Id = messageId,
            VisibleAfter = BaseTime.AddHours(1)
        });

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(0);
        dispatcher.DispatchedMessages.Should().BeEmpty();
        store.Get(messageId).Status.Should().Be(OutboxStatus.Pending);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenVisibleAfterReached_ShouldPublishMessage()
    {
        var clock = new OutboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new OutboxTests.InMemoryOutboxStore();
        await using var provider = BuildProcessorProvider(store, batchSize: 10, clock: clock);

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = provider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions
        {
            Id = messageId,
            VisibleAfter = BaseTime.AddMinutes(10)
        });

        clock.Advance(TimeSpan.FromMinutes(10));

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(1);
        dispatcher.DispatchedMessages.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenFixedBackoffConfigured_ShouldSetVisibleAfterToInitialDelay()
    {
        var clock = new OutboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new OutboxTests.InMemoryOutboxStore();
        await using var provider = BuildProcessorProvider(
            store,
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

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { Id = messageId });
        await processor.ProcessPendingAsync();

        store.Get(messageId).VisibleAfter.Should().Be(BaseTime.AddMinutes(2));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimStuckMessage()
    {
        var clock = new OutboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new OutboxTests.InMemoryOutboxStore();
        await using var provider = BuildProcessorProvider(store, batchSize: 10, clock: clock);

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = provider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { Id = messageId });

        await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-publisher",
            Now = BaseTime,
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        store.Get(messageId).Status.Should().Be(OutboxStatus.Publishing);

        clock.Advance(TimeSpan.FromMinutes(1));

        await processor.ProcessPendingAsync();

        dispatcher.DispatchedMessages.Should().ContainSingle();
        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);
        store.Get(messageId).AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherThrows_ShouldStoreErrorWithoutStackTrace()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        await using var provider = BuildProcessorProvider(
            store,
            batchSize: 10,
            useFailingDispatcher: true);

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { Id = messageId });
        await processor.ProcessPendingAsync();

        var lastError = store.Get(messageId).LastError;
        lastError.Should().Be($"{typeof(InvalidOperationException).FullName}: Simulated dispatcher failure.");
        lastError.Should().NotContain(" at ");
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldReturnLeasedCount()
    {
        var store = new OutboxTests.InMemoryOutboxStore();
        await using var provider = BuildProcessorProvider(store, batchSize: 10);

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        var emptyPass = await processor.ProcessPendingAsync();
        emptyPass.LeasedCount.Should().Be(0);

        await outbox.AddAsync(new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }, new OutboxOptions { Id = Guid.NewGuid() });

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldDispatchPocoMessageThroughMockDispatcher()
    {
        var store = new OutboxTests.InMemoryOutboxStore();

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
                    builder.Contracts.Register<PocoIntegrationEvent>("poco.events.sample", 1);
                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "poco-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });
            })
            .BuildServiceProvider();

        var writer = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = serviceProvider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>();
        var messageId = Guid.NewGuid();

        await writer.AddAsync(new PocoIntegrationEvent { Value = "poco-test" }, new OutboxOptions { Id = messageId });
        await processor.ProcessPendingAsync();

        dispatcher.DispatchedMessages
            .OfType<PocoIntegrationEvent>()
            .Should()
            .ContainSingle(poco => poco.Value == "poco-test");
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

    private static ServiceProvider BuildProcessorProvider(
        OutboxTests.InMemoryOutboxStore store,
        int batchSize,
        TimeProvider? clock = null,
        bool useFailingDispatcher = false,
        Action<OutboxModuleBuilder>? configureOutbox = null)
    {
        var services = new ServiceCollection()
            .AddSingleton<IOutboxStore>(store)
            .AddSingleton<IOutboxLeaseStore>(store)
            .AddSingleton<IOutboxStateStore>(store);

        if (useFailingDispatcher)
        {
            services.AddSingleton<IOutboxDispatcher>(new OutboxTests.AlwaysFailingOutboxDispatcher());
        }
        else
        {
            services.AddSingleton<OutboxTestInfrastructure.RecordingOutboxDispatcher>();
            services.AddSingleton<IOutboxDispatcher>(sp => sp.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcher>());
        }

        services.AddLiteBus(configuration =>
        {
            configuration.AddOutboxModule(outbox =>
            {
                if (configureOutbox is not null)
                {
                    configureOutbox(outbox);
                }
                else
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted", 1);
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
