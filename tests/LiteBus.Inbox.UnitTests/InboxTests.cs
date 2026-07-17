using LiteBus.Commands;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Dispatch.InProcess;
using LiteBus.Inbox.Storage.InMemory;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.DurableMessaging.Abstractions.Processing;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Inbox.UnitTests;

[Collection("Sequential")]
public sealed class InboxTests : LiteBusTestBase
{
    [Fact]
    public async Task AcceptAsync_ShouldStoreTypedEnvelopeAndReturnReceipt()
    {
        var now = new DateTimeOffset(2026, 5, 28, 10, 30, 0, TimeSpan.Zero);
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship", 2);

        var scheduler = InboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new ManualTimeProvider(now));

        var commandId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var receipt = await scheduler.AcceptAsync(InboxWriterTestFactory.Item(
            new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = $"ship:{orderId}"
            },
            InboxAcceptMetadata.Immediate with
            {
                Identity = new MessageIdentity.Supplied(commandId),
                Idempotency = new Idempotency.Keyed($"ship:{orderId}"),
                Trace = new MessageTrace.Workflow("correlation-1", "causation-1"),
                Tenant = new TenantScope.Isolated("tenant-1")
            }));

        receipt.Id.Should().Be(commandId);
        receipt.MessageType.Should().Be(typeof(InboxTestFixtures.ShipOrderCommand));
        receipt.Contract.Name.Should().Be("orders.commands.ship");
        receipt.Contract.Version.Should().Be(2);
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
        var recorder = new InboxTestFixtures.CommandRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                registry.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions
                        {
                            UseJitter = false
                        }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<InMemoryInboxStore>();

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();
        var orderId = Guid.NewGuid();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand {
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
        var recorder = new InboxTestFixtures.GenericCommandRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ArchiveCommand<string>>();
                    builder.Register<InboxTestFixtures.ArchiveStringCommandHandler>();
                });

                registry.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.ArchiveCommand<string>>("archive.commands.string");

                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "generic-test-worker",
                        Retry = new RetryOptions
                        {
                            UseJitter = false
                        }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<InMemoryInboxStore>();

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.ArchiveCommand<string> {
            Value = "closed-generic"
        });

        await processor.ProcessPendingAsync();

        recorder.Values.Should().ContainSingle(value => value == "closed-generic");
        store.Get(receipt.Id).Status.Should().Be(InboxStatus.Completed);
    }

    [Fact]
    public async Task AcceptAsync_WhenIdempotencyKeyMatchesExisting_ShouldReturnExistingReceipt()
    {
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

        var scheduler = InboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var orderId = Guid.NewGuid();
        var idempotencyKey = $"ship:{orderId}";
        var metadata = InboxAcceptMetadata.Immediate with { Idempotency = new Idempotency.Keyed(idempotencyKey) };

        var first = await scheduler.AcceptAsync(InboxWriterTestFactory.Item(
            new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = orderId,
                IdempotencyKey = idempotencyKey
            },
            metadata));

        var second = await scheduler.AcceptAsync(InboxWriterTestFactory.Item(
            new InboxTestFixtures.ShipOrderCommand
            {
                OrderId = Guid.NewGuid(),
                IdempotencyKey = idempotencyKey
            },
            metadata));

        first.Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        second.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        second.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task AcceptAsync_WhenMessageIdMatchesExisting_ShouldReturnExistingOutcome()
    {
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");
        var scheduler = InboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);
        var messageId = Guid.NewGuid();

        var first = await scheduler.AcceptAsync(InboxAcceptItem<InboxTestFixtures.ShipOrderCommand>.WithIdentity(
            new InboxTestFixtures.ShipOrderCommand { OrderId = Guid.NewGuid(), IdempotencyKey = "first" },
            messageId));
        var duplicate = await scheduler.AcceptAsync(InboxAcceptItem<InboxTestFixtures.ShipOrderCommand>.WithIdentity(
            new InboxTestFixtures.ShipOrderCommand { OrderId = Guid.NewGuid(), IdempotencyKey = "duplicate" },
            messageId));

        first.Outcome.Should().Be(InboxAcceptOutcome.Accepted);
        duplicate.Outcome.Should().Be(InboxAcceptOutcome.AlreadyAccepted);
        duplicate.Id.Should().Be(messageId);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerThrows_ShouldMarkFailedAndSetVisibleAfter()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.FaultyCommand>();
                    builder.Register<InboxTestFixtures.FaultyCommandHandler>();
                });

                registry.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.FaultyCommand>("orders.commands.faulty");

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

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<InMemoryInboxStore>();

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
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.FaultyCommand>();
                    builder.Register<InboxTestFixtures.FaultyCommandHandler>();
                });

                registry.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.FaultyCommand>("orders.commands.faulty");

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

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<InMemoryInboxStore>();

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
        var capture = new InboxTestFixtures.IsInboxCapture();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(capture)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.InboxCheckCommand>();
                    builder.Register<InboxTestFixtures.InboxCheckCommandHandler>();
                });

                registry.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.InboxCheckCommand>("test.commands.inbox-check");

                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
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
        var capture = new InboxTestFixtures.TraceMetadataCapture();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(capture)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.InboxCheckCommand>();
                    builder.Register<InboxTestFixtures.TraceMetadataCommandHandler>();
                });

                registry.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.InboxCheckCommand>("test.commands.inbox-check");

                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        await scheduler.AcceptAsync(InboxAcceptItem<InboxTestFixtures.InboxCheckCommand>.From(
            new InboxTestFixtures.InboxCheckCommand(),
            InboxAcceptMetadata.Immediate with
            {
                Trace = new MessageTrace.Workflow("correlation-42", "causation-42"),
                Tenant = new TenantScope.Isolated("tenant-42")
            }));

        await processor.ProcessPendingAsync();

        capture.CorrelationId.Should().Be("correlation-42");
        capture.CausationId.Should().Be("causation-42");
        capture.TenantId.Should().Be("tenant-42");
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerThrows_ShouldStoreTypeAndMessageOnlyInLastError()
    {
        var serviceProvider = new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(_ =>
                {
                });

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.FaultyCommand>();
                    builder.Register<InboxTestFixtures.FaultyCommandHandler>();
                });

                registry.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.FaultyCommand>("orders.commands.faulty");

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

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                });
            })
            .BuildServiceProvider();

        var store = serviceProvider.GetRequiredService<InMemoryInboxStore>();

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
        var recorder = new InboxTestFixtures.CommandRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessageModule(message => message.UseTimeProvider(clock));

                registry.AddCommandModule(builder =>
                {
                    builder.Register<InboxTestFixtures.ShipOrderCommand>();
                    builder.Register<InboxTestFixtures.ShipOrderCommandHandler>();
                });

                registry.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxTestFixtures.ShipOrderCommand>("orders.commands.ship");

                    builder.UseProcessorOptions(new InboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        LeaseDuration = TimeSpan.FromSeconds(30),
                        Retry = new RetryOptions { UseJitter = false }
                    });

                    builder.UseInMemoryStorage();
                    builder.UseInProcessDispatch();
                });
            })
            .AddSingleton<IInboxStateWriter>(sp =>
                new InboxTestFixtures.FlakyInboxStateStore(sp.GetRequiredService<InMemoryInboxStore>(), 1))
            .BuildServiceProvider();

        var inner = serviceProvider.GetRequiredService<InMemoryInboxStore>();

        var scheduler = serviceProvider.GetRequiredService<IInbox>();
        var processor = serviceProvider.GetRequiredService<IInboxProcessor>();

        var orderId = Guid.NewGuid();

        var receipt = await scheduler.AcceptAsync(new InboxTestFixtures.ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }).ConfigureAwait(false);

        var act = async () => await processor.ProcessPendingAsync().ConfigureAwait(false);

        await act.Should().NotThrowAsync().ConfigureAwait(false);

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
    public async Task AcceptBatchAsync_ShouldUseRuntimeTypeNotDeclaredGenericParameter()
    {
        var store = new InMemoryInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<BaseInboxCommand>("orders.commands.base");
        contractRegistry.Register<DerivedInboxCommand>("orders.commands.derived");

        var inbox = InboxWriterTestFactory.Create(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var receipts = await inbox.AcceptBatchAsync(
        [
            InboxAcceptItem.From(new DerivedInboxCommand { Marker = "derived" })
        ]);

        receipts.Should().ContainSingle();
        receipts[0].Contract.Name.Should().Be("orders.commands.derived");
        store.Get(receipts[0].Id).ContractName.Should().Be("orders.commands.derived");
    }

    [Fact]
    public void InboxProcessor_WithInvalidMaxAttempts_ShouldThrow()
    {
        var store = new InMemoryInboxStore();

        var act = () => new PipelinedInboxProcessor(
            store,
            store,
            new InboxTestFixtures.StubInboxDispatcher(),
            new InboxProcessorOptions
            {
                Retry = new RetryOptions { MaxAttempts = 0 }
            },
            TimeProvider.System,
            []);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private abstract record BaseInboxCommand;

    private sealed record DerivedInboxCommand : BaseInboxCommand
    {
        public string Marker { get; init; } = string.Empty;
    }
}
