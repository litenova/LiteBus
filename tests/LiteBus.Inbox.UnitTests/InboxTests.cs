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
public sealed class InboxTests : LiteBusTestBase
{
    [Fact]
    public async Task ScheduleAsync_ShouldStoreTypedEnvelopeAndReturnReceipt()
    {
        var now = new DateTimeOffset(2026, 5, 28, 10, 30, 0, TimeSpan.Zero);
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 2);

        var scheduler = new Inbox(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new ManualTimeProvider(now));

        var commandId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }, new InboxOptions
        {
            Id = commandId,
            IdempotencyKey = $"ship:{orderId}",
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            TenantId = "tenant-1"
        });

        receipt.Id.Should().Be(commandId);
        receipt.MessageType.Should().Be(typeof(InboxTestFixtures.ShipOrderCommand));
        receipt.ContractName.Should().Be("orders.commands.ship");
        receipt.ContractVersion.Should().Be(2);
        receipt.AcceptedAt.Should().Be(now);

        var envelope = store.Get(commandId);
        envelope.ContractName.Should().Be("orders.commands.ship");
        envelope.ContractVersion.Should().Be(2);
        envelope.Status.Should().Be(InboxStatus.Pending);
        envelope.AttemptCount.Should().Be(0);
        envelope.IdempotencyKey.Should().Be($"ship:{orderId}");
        envelope.CorrelationId.Should().Be("correlation-1");
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldExecuteCommandThroughMediatorAndMarkCompleted()
    {
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.CommandRecorder();

        var serviceProvider = new ServiceCollection()
            .AddInboxStoreRoles(store)
            .AddSingleton(recorder)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                modules.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions
                        {
                            UseJitter = false
                        }
                    });
                });
            })
            .BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();
        var orderId = Guid.NewGuid();
        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        await processor.ProcessPendingAsync();

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);

        var envelope = store.Get(receipt.Id);
        envelope.Status.Should().Be(InboxStatus.Completed);
        envelope.AttemptCount.Should().Be(1);
        envelope.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldSupportClosedGenericCommands()
    {
        var store = new InMemoryInboxStore();
        var recorder = new InboxTestFixtures.GenericCommandRecorder();

        var serviceProvider = new ServiceCollection()
            .AddInboxStoreRoles(store)
            .AddSingleton(recorder)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ArchiveCommand<string>>();
                    builder.Register<InboxTestFixtures.ArchiveStringCommandHandler>();
                });

                modules.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.ArchiveCommand<string>>("archive.commands.string", 1);
                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "generic-test-worker",
                        Retry = new RetryOptions
                        {
                            UseJitter = false
                        }
                    });
                });
            })
            .BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.ArchiveCommand<string>
        {
            Value = "closed-generic"
        });

        await processor.ProcessPendingAsync();

        recorder.Values.Should().ContainSingle(value => value == "closed-generic");
        store.Get(receipt.Id).Status.Should().Be(InboxStatus.Completed);
    }

    [Fact]
    public async Task ScheduleAsync_WhenIdempotencyKeyMatchesExisting_ShouldReturnExistingReceipt()
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
        var idempotencyKey = $"ship:{orderId}";

        var first = await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = idempotencyKey
        }, new InboxOptions { IdempotencyKey = idempotencyKey });

        var second = await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey
        }, new InboxOptions { IdempotencyKey = idempotencyKey });

        second.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerThrows_ShouldMarkFailedAndSetVisibleAfter()
    {
        var store = new InMemoryInboxStore();

        var serviceProvider = new ServiceCollection()
            .AddInboxStoreRoles(store)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.FaultyCommand>();
                    builder.Register<InboxTestFixtures.FaultyCommandHandler>();
                });

                modules.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.FaultyCommand>("orders.commands.faulty", 1);
                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
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

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.FaultyCommand());

        await processor.ProcessPendingAsync();

        var envelope = store.Get(receipt.Id);
        envelope.Status.Should().Be(InboxStatus.Failed);
        envelope.LastError.Should().NotBeNullOrWhiteSpace();
        envelope.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerExceedsMaxAttempts_ShouldMoveToDeadLetter()
    {
        var store = new InMemoryInboxStore();

        var serviceProvider = new ServiceCollection()
            .AddInboxStoreRoles(store)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.FaultyCommand>();
                    builder.Register<InboxTestFixtures.FaultyCommandHandler>();
                });

                modules.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.FaultyCommand>("orders.commands.faulty", 1);
                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
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

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.FaultyCommand());

        // Attempt 1 of 2: AttemptCount reaches 1 which is < MaxAttempts (2), so envelope is retried.
        await processor.ProcessPendingAsync();
        // Attempt 2 of 2: AttemptCount reaches 2 which is >= MaxAttempts (2), so envelope is dead-lettered.
        await processor.ProcessPendingAsync();

        store.Get(receipt.Id).Status.Should().Be(InboxStatus.DeadLettered);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldSetIsInboxExecutionContextKey()
    {
        var store = new InMemoryInboxStore();
        var capture = new InboxTestFixtures.IsInboxCapture();

        var serviceProvider = new ServiceCollection()
            .AddInboxStoreRoles(store)
            .AddSingleton(capture)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.InboxCheckCommand>();
                    builder.Register<InboxTestFixtures.InboxCheckCommandHandler>();
                });

                modules.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.InboxCheckCommand>("test.commands.inbox-check", 1);
                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });
            })
            .BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        await scheduler.AcceptAsync(new InboxTestFixtures.InboxCheckCommand());
        await processor.ProcessPendingAsync();

        capture.IsInboxExecution.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldPropagateTraceMetadataToExecutionContext()
    {
        var store = new InMemoryInboxStore();
        var capture = new InboxTestFixtures.TraceMetadataCapture();

        var serviceProvider = new ServiceCollection()
            .AddInboxStoreRoles(store)
            .AddSingleton(capture)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.InboxCheckCommand>();
                    builder.Register<InboxTestFixtures.TraceMetadataCommandHandler>();
                });

                modules.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.InboxCheckCommand>("test.commands.inbox-check", 1);
                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });
            })
            .BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        await scheduler.AcceptAsync(new InboxTestFixtures.InboxCheckCommand(), new InboxOptions
        {
            CorrelationId = "correlation-42",
            CausationId = "causation-42",
            TenantId = "tenant-42"
        });

        await processor.ProcessPendingAsync();

        capture.CorrelationId.Should().Be("correlation-42");
        capture.CausationId.Should().Be("causation-42");
        capture.TenantId.Should().Be("tenant-42");
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerThrows_ShouldStoreTypeAndMessageOnlyInLastError()
    {
        var store = new InMemoryInboxStore();

        var serviceProvider = new ServiceCollection()
            .AddInboxStoreRoles(store)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.FaultyCommand>();
                    builder.Register<InboxTestFixtures.FaultyCommandHandler>();
                });

                modules.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.FaultyCommand>("orders.commands.faulty", 1);
                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
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

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.FaultyCommand());
        await processor.ProcessPendingAsync();

        var lastError = store.Get(receipt.Id).LastError;
        lastError.Should().Be($"{typeof(InvalidOperationException).FullName}: Simulated handler failure.");
        lastError.Should().NotContain(" at ");
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenMarkCompletedFailsAfterSuccessfulDispatch_ShouldNotMarkFailed()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 5, 30, 12, 0, 0, TimeSpan.Zero));
        var inner = new InMemoryInboxStore();
        var flakyStateStore = new InboxTestFixtures.FlakyInboxStateStore(inner, failCompletionsBeforeSuccess: 2);
        var recorder = new InboxTestFixtures.CommandRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IInboxStore>(inner)
            .AddSingleton<IInboxLeaseStore>(inner)
            .AddSingleton<IInboxStateWriter>(flakyStateStore)
            .AddSingleton<IInboxRetentionStore>(inner)
            .AddSingleton<IInboxDiagnosticsStore>(inner)
            .AddSingleton(recorder)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(modules =>
            {
                modules.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                modules.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 1);
                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        LeaseDuration = TimeSpan.FromSeconds(30),
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });
            })
            .AddSingleton<TimeProvider>(clock)
            .BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        var orderId = Guid.NewGuid();
        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        var act = async () => await processor.ProcessPendingAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Simulated completion failure*");

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);

        var envelope = inner.Get(receipt.Id);
        envelope.Status.Should().Be(InboxStatus.Processing);
        envelope.LastError.Should().BeNull();
        envelope.AttemptCount.Should().Be(1);

        clock.Advance(TimeSpan.FromMinutes(1));

        await processor.ProcessPendingAsync();

        inner.Get(receipt.Id).Status.Should().Be(InboxStatus.Completed);
        recorder.Commands.Should().HaveCount(2);
    }

    [Fact]
    public void InboxProcessor_WithInvalidMaxAttempts_ShouldThrow()
    {
        var act = () => new InboxProcessor(
            new InMemoryInboxStore(),
            new InMemoryInboxStore(),
            new InboxTestFixtures.StubInboxDispatcher(),
            new InboxProcessorOptions
            {
                Retry = new RetryOptions { MaxAttempts = 0 }
            },
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}