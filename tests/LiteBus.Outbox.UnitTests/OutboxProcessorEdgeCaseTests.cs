using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.InMemory;
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
        var store = new InMemoryOutboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");

        var writer = OutboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var messageId = Guid.NewGuid();

        var receipt = await writer.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            messageId));

        receipt.Id.Should().Be(messageId);
        store.Get(messageId).Id.Should().Be(messageId);
    }

    [Fact]
    public async Task AddAsync_WhenMessageIdOmitted_ShouldGenerateId()
    {
        var store = new InMemoryOutboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");

        var writer = OutboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var receipt = await writer.EnqueueAsync(OutboxWriterTestFactory.Item(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() }));

        receipt.Id.Should().NotBe(Guid.Empty);
        store.Get(receipt.Id).Id.Should().Be(receipt.Id);
    }

    [Fact]
    public async Task AddAsync_WhenDuplicateMessageId_ShouldReturnExistingEnvelope()
    {
        var store = new InMemoryOutboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");

        var writer = OutboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var messageId = Guid.NewGuid();
        var firstOrderId = Guid.NewGuid();
        var secondOrderId = Guid.NewGuid();

        var first = await writer.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = firstOrderId },
            messageId)).ConfigureAwait(true);


        var duplicate = await writer.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = secondOrderId },
            messageId)).ConfigureAwait(true);
        first.Outcome.Should().Be(OutboxEnqueueOutcome.Enqueued);
        duplicate.Outcome.Should().Be(OutboxEnqueueOutcome.AlreadyEnqueued);
        duplicate.Id.Should().Be(first.Id);
        store.GetAll().Should().HaveCount(1);
        store.Get(messageId).Topic.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_WhenContractNotRegistered_ShouldThrowMessageContractNotRegisteredException()
    {
        var store = new InMemoryOutboxStore();

        var writer = OutboxWriterTestFactory.Create(
            store,
            new MessageContractRegistry(),
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var act = async () => await writer.EnqueueAsync(OutboxWriterTestFactory.Item(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() })).ConfigureAwait(true);

        await act.Should().ThrowAsync<MessageContractNotRegisteredException>();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldProcessMultipleMessagesInSinglePass()
    {
         var provider = BuildProcessorProvider(10);
         await using (provider.ConfigureAwait(true))
         {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = provider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcherHolder>().Instance!;

        for (var i = 0; i < 3; i++)
        {
            await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
                new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                Guid.NewGuid())).ConfigureAwait(true);

        }

        var pass = await processor.ProcessPendingAsync().ConfigureAwait(true);
        pass.LeasedCount.Should().Be(3);
        dispatcher.DispatchedMessages.Should().HaveCount(3);
        store.GetAll().Should().OnlyContain(envelope => envelope.Status == OutboxStatus.Published);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldRespectBatchSize()
    {
         var provider = BuildProcessorProvider(2);
         await using (provider.ConfigureAwait(true))
         {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = provider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcherHolder>().Instance!;

        for (var i = 0; i < 5; i++)
        {
            await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
                new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                Guid.NewGuid())).ConfigureAwait(true);

        }

        var firstPass = await processor.ProcessPendingAsync().ConfigureAwait(true);
        firstPass.LeasedCount.Should().Be(2);
        dispatcher.DispatchedMessages.Should().HaveCount(2);

        var secondPass = await processor.ProcessPendingAsync().ConfigureAwait(true);
        secondPass.LeasedCount.Should().Be(2);
        dispatcher.DispatchedMessages.Should().HaveCount(4);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseMessage()
    {
        var clock = new ManualTimeProvider(BaseTime);
         var provider = BuildProcessorProvider(10, clock);
         await using (provider.ConfigureAwait(true))
         {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = provider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcherHolder>().Instance!;
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithMetadata(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            OutboxEnqueueMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId),
                Visibility = new MessageVisibility.At(BaseTime.AddHours(1))
            })).ConfigureAwait(true);


        var pass = await processor.ProcessPendingAsync().ConfigureAwait(true);
        pass.LeasedCount.Should().Be(0);
        dispatcher.DispatchedMessages.Should().BeEmpty();
        store.Get(messageId).Status.Should().Be(OutboxStatus.Pending);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenVisibleAfterReached_ShouldPublishMessage()
    {
        var clock = new ManualTimeProvider(BaseTime);
         var provider = BuildProcessorProvider(10, clock);
         await using (provider.ConfigureAwait(true))
         {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = provider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcherHolder>().Instance!;
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithMetadata(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            OutboxEnqueueMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(messageId),
                Visibility = new MessageVisibility.At(BaseTime.AddMinutes(10))
            })).ConfigureAwait(true);


        clock.Advance(TimeSpan.FromMinutes(10));

        var pass = await processor.ProcessPendingAsync().ConfigureAwait(false);
        pass.LeasedCount.Should().Be(1);
        dispatcher.DispatchedMessages.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenFixedBackoffConfigured_ShouldSetVisibleAfterToInitialDelay()
    {
        var clock = new ManualTimeProvider(BaseTime);

        var provider = BuildProcessorProvider(
            10,
            clock,
            true,
            outbox =>
            {
                outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");

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
        await using (provider.ConfigureAwait(true))
        {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            messageId)).ConfigureAwait(true);


        await processor.ProcessPendingAsync().ConfigureAwait(false);

        store.Get(messageId).VisibleAfter.Should().Be(BaseTime.AddMinutes(2));
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimStuckMessage()
    {
        var clock = new ManualTimeProvider(BaseTime);
         var provider = BuildProcessorProvider(10, clock);
         await using (provider.ConfigureAwait(true))
         {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = provider.GetRequiredService<OutboxTestInfrastructure.RecordingOutboxDispatcherHolder>().Instance!;
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            messageId)).ConfigureAwait(true);


        await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-publisher",
            Now = BaseTime,
            LeaseDuration = TimeSpan.FromSeconds(30)
        }).ConfigureAwait(true);


        store.Get(messageId).Status.Should().Be(OutboxStatus.Publishing);

        clock.Advance(TimeSpan.FromMinutes(1));

        await processor.ProcessPendingAsync().ConfigureAwait(false);

        dispatcher.DispatchedMessages.Should().ContainSingle();
        store.Get(messageId).Status.Should().Be(OutboxStatus.Published);
        store.Get(messageId).AttemptCount.Should().Be(2);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenDispatcherThrows_ShouldStoreErrorWithoutStackTrace()
    {
        var provider = BuildProcessorProvider(
            10,
            useFailingDispatcher: true);
        await using (provider.ConfigureAwait(true))
        {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();
        var messageId = Guid.NewGuid();

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            messageId)).ConfigureAwait(true);


        await processor.ProcessPendingAsync().ConfigureAwait(true);

        var lastError = store.Get(messageId).LastError;
        lastError.Should().Be($"{typeof(InvalidOperationException).FullName}: Simulated dispatcher failure.");
        lastError.Should().NotContain(" at ");
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldReturnLeasedCount()
    {
         var provider = BuildProcessorProvider(10);
         await using (provider.ConfigureAwait(true))
         {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        var emptyPass = await processor.ProcessPendingAsync().ConfigureAwait(true);
        emptyPass.LeasedCount.Should().Be(0);

        await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
            Guid.NewGuid())).ConfigureAwait(false);


        var pass = await processor.ProcessPendingAsync().ConfigureAwait(true);
        pass.LeasedCount.Should().Be(1);
        }
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldDispatchPocoMessageThroughMockDispatcher()
    {
        var dispatcherHolder = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(dispatcherHolder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddOutboxModule(builder =>
                {
                    builder.Contracts.Register<PocoIntegrationEvent>("poco.events.sample");

                    builder.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "poco-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseRecordingOutboxDispatcher(dispatcherHolder);
                });
            })
            .BuildServiceProvider();

        var writer = serviceProvider.GetRequiredService<IOutbox>();
        var processor = serviceProvider.GetRequiredService<IOutboxProcessor>();
        var dispatcher = dispatcherHolder.Instance!;
        var messageId = Guid.NewGuid();

        await writer.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
            new PocoIntegrationEvent { Value = "poco-test" },
            messageId)).ConfigureAwait(false);


        await processor.ProcessPendingAsync().ConfigureAwait(false);

        dispatcher.DispatchedMessages
            .OfType<PocoIntegrationEvent>()
            .Should()
            .ContainSingle(poco => poco.Value == "poco-test");
    }

    [Fact]
    public void OutboxProcessor_WithInvalidBatchSize_ShouldThrow()
    {
        var store = new InMemoryOutboxStore();

        var act = () => new PipelinedOutboxProcessor(
            store,
            store,
            new OutboxTests.AlwaysFailingOutboxDispatcher(),
            new OutboxProcessorOptions { BatchSize = 0 },
            TimeProvider.System,
            Array.Empty<IProcessorEnvelopeHook>());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OutboxProcessor_WithInvalidLeaseDuration_ShouldThrow()
    {
        var store = new InMemoryOutboxStore();

        var act = () => new PipelinedOutboxProcessor(
            store,
            store,
            new OutboxTests.AlwaysFailingOutboxDispatcher(),
            new OutboxProcessorOptions { LeaseDuration = TimeSpan.Zero },
            TimeProvider.System,
            Array.Empty<IProcessorEnvelopeHook>());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OutboxProcessor_WithInvalidMaxAttempts_ShouldThrow()
    {
        var store = new InMemoryOutboxStore();

        var act = () => new PipelinedOutboxProcessor(
            store,
            store,
            new OutboxTests.AlwaysFailingOutboxDispatcher(),
            new OutboxProcessorOptions { Retry = new RetryOptions { MaxAttempts = 0 } },
            TimeProvider.System,
            Array.Empty<IProcessorEnvelopeHook>());

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenCancellationRequested_ShouldPropagateOperationCanceledException()
    {
         var provider = BuildProcessorProvider(10);
         await using (provider.ConfigureAwait(true))
         {
        var store = provider.GetRequiredService<InMemoryOutboxStore>();

        var outbox = provider.GetRequiredService<IOutbox>();
        var processor = provider.GetRequiredService<IOutboxProcessor>();

        for (var i = 0; i < 3; i++)
        {
            await outbox.EnqueueAsync(OutboxWriterTestFactory.ItemWithId(
                new OutboxTests.OrderSubmittedIntegrationEvent { OrderId = Guid.NewGuid() },
                Guid.NewGuid())).ConfigureAwait(true);

        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await processor.ProcessPendingAsync(cts.Token).ConfigureAwait(false);

        await act.Should().ThrowAsync<OperationCanceledException>();
        }
    }

    private static ServiceProvider BuildProcessorProvider(
        int batchSize,
        TimeProvider? clock = null,
        bool useFailingDispatcher = false,
        Action<OutboxModuleBuilder>? configureOutbox = null)
    {
        var dispatcherHolder = new OutboxTestInfrastructure.RecordingOutboxDispatcherHolder();
        var services = new ServiceCollection().AddSingleton(dispatcherHolder);

        services.AddLiteBus(registry =>
        {
            registry.AddMessageModule(message =>
            {
                if (clock is not null)
                {
                    message.UseTimeProvider(clock);
                }
            });

            registry.AddOutboxModule(outbox =>
            {
                if (configureOutbox is not null)
                {
                    configureOutbox(outbox);
                }
                else
                {
                    outbox.Contracts.Register<OutboxTests.OrderSubmittedIntegrationEvent>("orders.events.submitted");

                    outbox.UseProcessorOptions(new OutboxProcessorOptions
                    {
                        BatchSize = batchSize,
                        LeaseOwner = "test-publisher",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                }

                outbox.UseInMemoryStorage();

                if (useFailingDispatcher)
                {
                    outbox.UseFixedOutboxDispatcher(new OutboxTests.AlwaysFailingOutboxDispatcher());
                }
                else
                {
                    outbox.UseRecordingOutboxDispatcher(dispatcherHolder);
                }
            });
        });

        return services.BuildServiceProvider();
    }

    public sealed record PocoIntegrationEvent
    {
        public required string Value { get; init; }
    }
}
