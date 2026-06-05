using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests;

[Collection("Sequential")]
public sealed class InboxProcessorEdgeCaseTests : LiteBusTestBase
{
    private static readonly DateTimeOffset BaseTime = new(2026, 5, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScheduleAsync_ShouldPersistVisibleAfterFromOptions()
    {
        var visibleAfter = BaseTime.AddHours(1);
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);

        var scheduler = new Inbox(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new InboxTestInfrastructure.ManualTimeProvider(BaseTime));

        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "ship-visible"
        }, new InboxOptions { VisibleAfter = visibleAfter });

        store.GetAll().Single().VisibleAfter.Should().Be(visibleAfter);
    }

    [Fact]
    public async Task ScheduleAsync_ShouldStoreIdempotencyKeyFromOptions()
    {
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);

        var scheduler = new Inbox(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var orderId = Guid.NewGuid();
        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }, new InboxOptions { IdempotencyKey = $"ship:{orderId}" });

        var envelope = store.GetAll().Single();
        envelope.IdempotencyKey.Should().Be($"ship:{orderId}");
    }

    [Fact]
    public async Task ScheduleAsync_WhenContractNotRegistered_ShouldThrowMessageContractNotRegisteredException()
    {
        var store = new InMemoryInboxStore();
        var scheduler = new Inbox(
            store,
            new MessageContractRegistry(),
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var act = async () => await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "missing-contract"
        });

        await act.Should().ThrowAsync<MessageContractNotRegisteredException>();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldProcessMultipleCommandsInSinglePass()
    {
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        for (var i = 0; i < 3; i++)
        {
            var orderId = Guid.NewGuid();
            await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            });
        }

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(3);
        recorder.Commands.Should().HaveCount(3);
        store.GetAll().Should().OnlyContain(envelope => envelope.Status == InboxStatus.Completed);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldRespectBatchSize()
    {
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 2);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        for (var i = 0; i < 5; i++)
        {
            var orderId = Guid.NewGuid();
            await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            });
        }

        var firstPass = await processor.ProcessPendingAsync();
        firstPass.LeasedCount.Should().Be(2);
        recorder.Commands.Should().HaveCount(2);

        var secondPass = await processor.ProcessPendingAsync();
        secondPass.LeasedCount.Should().Be(2);
        recorder.Commands.Should().HaveCount(4);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseCommand()
    {
        var clock = new InboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10, clock: clock);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "future"
        }, new InboxOptions { VisibleAfter = BaseTime.AddMinutes(30) });

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(0);
        recorder.Commands.Should().BeEmpty();
        store.GetAll().Single().Status.Should().Be(InboxStatus.Pending);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenVisibleAfterReached_ShouldProcessCommand()
    {
        var clock = new InboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10, clock: clock);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "due-later"
        }, new InboxOptions { VisibleAfter = BaseTime.AddMinutes(5) });

        clock.Advance(TimeSpan.FromMinutes(5));

        var pass = await processor.ProcessPendingAsync();
        pass.LeasedCount.Should().Be(1);
        recorder.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenFixedBackoffConfigured_ShouldSetVisibleAfterToInitialDelay()
    {
        var clock = new InboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new InMemoryInboxStore();
        await using var provider = BuildProcessorProvider(
            store,
            recorder: null,
            batchSize: 10,
            clock: clock,
            configureInbox: inbox =>
            {
                inbox.Contracts.Register<InboxTestFixtures.FaultyCommand>("orders.commands.faulty", 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "test-worker",
                    Retry = new RetryOptions
                    {
                        MaxAttempts = 5,
                        InitialDelay = TimeSpan.FromSeconds(30),
                        Backoff = RetryBackoff.Fixed,
                        UseJitter = false
                    }
                });
            },
            registerFaultyHandler: true);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.FaultyCommand());
        await processor.ProcessPendingAsync();

        store.Get(receipt.Id).VisibleAfter.Should().Be(BaseTime.AddSeconds(30));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenExponentialBackoffConfigured_ShouldDoubleDelayPerAttempt()
    {
        var clock = new InboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new InMemoryInboxStore();
        await using var provider = BuildProcessorProvider(
            store,
            recorder: null,
            batchSize: 10,
            clock: clock,
            configureInbox: inbox =>
            {
                inbox.Contracts.Register<InboxTestFixtures.FaultyCommand>("orders.commands.faulty", 1);
                inbox.UseProcessorOptions(new InboxProcessorOptions
                {
                    BatchSize = 10,
                    LeaseOwner = "test-worker",
                    Retry = new RetryOptions
                    {
                        MaxAttempts = 5,
                        InitialDelay = TimeSpan.FromSeconds(10),
                        MaxDelay = TimeSpan.FromHours(1),
                        Backoff = RetryBackoff.Exponential,
                        UseJitter = false
                    }
                });
            },
            registerFaultyHandler: true);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.FaultyCommand());
        await processor.ProcessPendingAsync();

        store.Get(receipt.Id).VisibleAfter.Should().Be(BaseTime.AddSeconds(10));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimStuckCommand()
    {
        var clock = new InboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10, clock: clock);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "lease-expiry"
        });

        await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-worker",
            Now = BaseTime,
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        store.Get(receipt.Id).Status.Should().Be(InboxStatus.Processing);

        clock.Advance(TimeSpan.FromMinutes(1));

        await processor.ProcessPendingAsync();

        recorder.Commands.Should().ContainSingle();
        store.Get(receipt.Id).Status.Should().Be(InboxStatus.Completed);
        store.Get(receipt.Id).AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenContractNameUnknown_ShouldMarkFailed()
    {
        var store = new InMemoryInboxStore();
        await using var provider = BuildProcessorProvider(store, new InboxTestFixtures.CommandRecorder(), batchSize: 10);

        var processor = provider.GetRequiredService<IInboxProcessor>();
        var commandId = Guid.NewGuid();

        await store.AddAsync(new InboxEnvelope
        {
            Id = commandId,
            ContractName = "unknown.contract",
            ContractVersion = 99,
            Payload = "{}",
            CreatedAt = BaseTime,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        });

        await processor.ProcessPendingAsync();

        store.Get(commandId).Status.Should().Be(InboxStatus.Failed);
        store.Get(commandId).LastError.Should().Contain(nameof(MessageContractNotRegisteredException));
    }

    [Fact]
    public void InboxProcessor_WithInvalidBatchSize_ShouldThrow()
    {
        var act = () => new InboxProcessor(
            new InMemoryInboxStore(),
            new InMemoryInboxStore(),
            new InboxTestFixtures.StubInboxDispatcher(),
            new InboxProcessorOptions { BatchSize = 0 },
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InboxProcessor_WithInvalidLeaseDuration_ShouldThrow()
    {
        var act = () => new InboxProcessor(
            new InMemoryInboxStore(),
            new InMemoryInboxStore(),
            new InboxTestFixtures.StubInboxDispatcher(),
            new InboxProcessorOptions { LeaseDuration = TimeSpan.Zero },
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenCancellationRequested_ShouldPropagateOperationCanceledException()
    {
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<IInboxProcessor>();

        for (var i = 0; i < 3; i++)
        {
            var orderId = Guid.NewGuid();
            await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            });
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await processor.ProcessPendingAsync(cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static ServiceProvider BuildProcessorProvider(
        InMemoryInboxStore store,
        InboxTestFixtures.CommandRecorder? recorder,
        int batchSize,
        TimeProvider? clock = null,
        Action<InboxModuleBuilder>? configureInbox = null,
        bool registerFaultyHandler = false)
    {
        var services = new ServiceCollection()
            .AddInboxStoreRoles(store);

        if (recorder is not null)
        {
            services.AddSingleton(recorder);
        }

        services.AddCommandMediatorInboxDispatcher();

        services.AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    if (registerFaultyHandler)
                    {
                        builder.Register<InboxTestFixtures.FaultyCommand>();
                        builder.Register<InboxTestFixtures.FaultyCommandHandler>();
                    }
                    else
                    {
                        builder.Register<InboxTestFixtures.ShipOrderCommand>();
                        builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                    }
                });

                modules.AddInboxModule(inbox =>
                {
                    if (configureInbox is not null)
                    {
                        configureInbox(inbox);
                    }
                    else
                    {
                        inbox.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                        inbox.UseProcessorOptions(new InboxProcessorOptions
                        {
                            BatchSize = batchSize,
                            LeaseOwner = "test-worker",
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

