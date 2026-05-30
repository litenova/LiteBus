using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

using CommandInboxProcessorContract = LiteBus.Inbox.Abstractions.IInboxProcessor;

namespace LiteBus.Inbox.UnitTests;

[Collection("Sequential")]
public sealed class CommandInboxProcessorEdgeCaseTests : LiteBusTestBase
{
    private static readonly DateTimeOffset BaseTime = new(2026, 5, 29, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ScheduleAsync_ShouldPersistVisibleAfterFromOptions()
    {
        var visibleAfter = BaseTime.AddHours(1);
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);

        var scheduler = new InboxWriter(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new InboxTestInfrastructure.ManualTimeProvider(BaseTime));

        await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "ship-visible"
        }, new InboxOptions { VisibleAfter = visibleAfter });

        store.GetAll().Single().VisibleAfter.Should().Be(visibleAfter);
    }

    [Fact]
    public async Task ScheduleAsync_ShouldStoreIdempotencyKeyFromOptions()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);

        var scheduler = new InboxWriter(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var orderId = Guid.NewGuid();
        await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
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
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var scheduler = new InboxWriter(
            store,
            new MessageContractRegistry(),
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var act = async () => await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = "missing-contract"
        });

        await act.Should().ThrowAsync<MessageContractNotRegisteredException>();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldProcessMultipleCommandsInSinglePass()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<CommandInboxProcessorContract>();

        for (var i = 0; i < 3; i++)
        {
            var orderId = Guid.NewGuid();
            await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
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
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 2);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<CommandInboxProcessorContract>();

        for (var i = 0; i < 5; i++)
        {
            var orderId = Guid.NewGuid();
            await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
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
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10, clock: clock);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<CommandInboxProcessorContract>();

        await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
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
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10, clock: clock);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<CommandInboxProcessorContract>();

        await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
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
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        await using var provider = BuildProcessorProvider(
            store,
            recorder: null,
            batchSize: 10,
            clock: clock,
            configureInbox: inbox =>
            {
                inbox.Contracts.Register<CommandInboxTests.FaultyCommand>("orders.commands.faulty", 1);
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
        var processor = provider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.AddAsync(new CommandInboxTests.FaultyCommand());
        await processor.ProcessPendingAsync();

        store.Get(receipt.Id).VisibleAfter.Should().Be(BaseTime.AddSeconds(30));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenExponentialBackoffConfigured_ShouldDoubleDelayPerAttempt()
    {
        var clock = new InboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        await using var provider = BuildProcessorProvider(
            store,
            recorder: null,
            batchSize: 10,
            clock: clock,
            configureInbox: inbox =>
            {
                inbox.Contracts.Register<CommandInboxTests.FaultyCommand>("orders.commands.faulty", 1);
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
        var processor = provider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.AddAsync(new CommandInboxTests.FaultyCommand());
        await processor.ProcessPendingAsync();

        store.Get(receipt.Id).VisibleAfter.Should().Be(BaseTime.AddSeconds(10));
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenLeaseExpires_ShouldReclaimStuckCommand()
    {
        var clock = new InboxTestInfrastructure.ManualTimeProvider(BaseTime);
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10, clock: clock);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
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
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        await using var provider = BuildProcessorProvider(store, new CommandInboxTests.CommandRecorder(), batchSize: 10);

        var processor = provider.GetRequiredService<CommandInboxProcessorContract>();
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
            new CommandInboxTests.InMemoryCommandInboxStore(),
            new CommandInboxTests.InMemoryCommandInboxStore(),
            new CommandInboxTests.StubInboxDispatcher(),
            new InboxProcessorOptions { BatchSize = 0 },
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void InboxProcessor_WithInvalidLeaseDuration_ShouldThrow()
    {
        var act = () => new InboxProcessor(
            new CommandInboxTests.InMemoryCommandInboxStore(),
            new CommandInboxTests.InMemoryCommandInboxStore(),
            new CommandInboxTests.StubInboxDispatcher(),
            new InboxProcessorOptions { LeaseDuration = TimeSpan.Zero },
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenCancellationRequested_ShouldPropagateOperationCanceledException()
    {
        var store = new CommandInboxTests.InMemoryCommandInboxStore();
        var recorder = new CommandInboxTests.CommandRecorder();
        await using var provider = BuildProcessorProvider(store, recorder, batchSize: 10);

        var scheduler = provider.GetRequiredService<IInbox>();
        var processor = provider.GetRequiredService<CommandInboxProcessorContract>();

        for (var i = 0; i < 3; i++)
        {
            var orderId = Guid.NewGuid();
            await scheduler.AddAsync(new CommandInboxTests.ShipOrderCommand
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
        CommandInboxTests.InMemoryCommandInboxStore store,
        CommandInboxTests.CommandRecorder? recorder,
        int batchSize,
        TimeProvider? clock = null,
        Action<InboxModuleBuilder>? configureInbox = null,
        bool registerFaultyHandler = false)
    {
        var services = new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(store)
            .AddSingleton<IInboxStateStore>(store);

        if (recorder is not null)
        {
            services.AddSingleton(recorder);
        }

        services.AddCommandMediatorInboxDispatcher();

        services.AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    if (registerFaultyHandler)
                    {
                        builder.Register<CommandInboxTests.FaultyCommand>();
                        builder.Register<CommandInboxTests.FaultyCommandHandler>();
                    }
                    else
                    {
                        builder.Register<CommandInboxTests.ShipOrderCommand>();
                        builder.Register<CommandInboxTests.ShipOrderCommandHandler>();
                    }
                });

                configuration.AddInboxModule(inbox =>
                {
                    if (configureInbox is not null)
                    {
                        configureInbox(inbox);
                    }
                    else
                    {
                        inbox.Contracts.Register<CommandInboxTests.ShipOrderCommand>("orders.commands.ship", 1);
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
