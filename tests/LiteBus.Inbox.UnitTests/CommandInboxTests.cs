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
public sealed class CommandInboxTests : LiteBusTestBase
{
    [Fact]
    public async Task ScheduleAsync_ShouldStoreTypedEnvelopeAndReturnReceipt()
    {
        var now = new DateTimeOffset(2026, 5, 28, 10, 30, 0, TimeSpan.Zero);
        var store = new InMemoryCommandInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ShipOrderCommand>("orders.commands.ship", 2);

        var scheduler = new InboxWriter(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new FixedTimeProvider(now));

        var commandId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var receipt = await scheduler.AddAsync(new ShipOrderCommand
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
        receipt.MessageType.Should().Be(typeof(ShipOrderCommand));
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
        var store = new InMemoryCommandInboxStore();
        var recorder = new CommandRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(store)
            .AddSingleton<IInboxStateStore>(store)
            .AddSingleton(recorder)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                });

                configuration.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
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
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();
        var orderId = Guid.NewGuid();
        var receipt = await scheduler.AddAsync(new ShipOrderCommand
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
        var store = new InMemoryCommandInboxStore();
        var recorder = new GenericCommandRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(store)
            .AddSingleton<IInboxStateStore>(store)
            .AddSingleton(recorder)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<ArchiveCommand<string>>();
                    builder.Register<ArchiveStringCommandHandler>();
                });

                configuration.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<ArchiveCommand<string>>("archive.commands.string", 1);
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
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.AddAsync(new ArchiveCommand<string>
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
        var store = new InMemoryCommandInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ShipOrderCommand>("orders.commands.ship", 1);

        var scheduler = new InboxWriter(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var orderId = Guid.NewGuid();
        var idempotencyKey = $"ship:{orderId}";

        var first = await scheduler.AddAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = idempotencyKey
        }, new InboxOptions { IdempotencyKey = idempotencyKey });

        var second = await scheduler.AddAsync(new ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey
        }, new InboxOptions { IdempotencyKey = idempotencyKey });

        second.Id.Should().Be(first.Id);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerThrows_ShouldMarkFailedAndSetVisibleAfter()
    {
        var store = new InMemoryCommandInboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(store)
            .AddSingleton<IInboxStateStore>(store)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<FaultyCommand>();
                    builder.Register<FaultyCommandHandler>();
                });

                configuration.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<FaultyCommand>("orders.commands.faulty", 1);
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
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.AddAsync(new FaultyCommand());

        await processor.ProcessPendingAsync();

        var envelope = store.Get(receipt.Id);
        envelope.Status.Should().Be(InboxStatus.Failed);
        envelope.LastError.Should().NotBeNullOrWhiteSpace();
        envelope.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerExceedsMaxAttempts_ShouldMoveToDeadLetter()
    {
        var store = new InMemoryCommandInboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(store)
            .AddSingleton<IInboxStateStore>(store)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<FaultyCommand>();
                    builder.Register<FaultyCommandHandler>();
                });

                configuration.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<FaultyCommand>("orders.commands.faulty", 1);
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
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.AddAsync(new FaultyCommand());

        // Attempt 1 of 2: AttemptCount reaches 1 which is < MaxAttempts (2), so envelope is retried.
        await processor.ProcessPendingAsync();
        // Attempt 2 of 2: AttemptCount reaches 2 which is >= MaxAttempts (2), so envelope is dead-lettered.
        await processor.ProcessPendingAsync();

        store.Get(receipt.Id).Status.Should().Be(InboxStatus.DeadLettered);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldSetIsInboxExecutionContextKey()
    {
        var store = new InMemoryCommandInboxStore();
        var capture = new IsInboxCapture();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(store)
            .AddSingleton<IInboxStateStore>(store)
            .AddSingleton(capture)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<InboxCheckCommand>();
                    builder.Register<InboxCheckCommandHandler>();
                });

                configuration.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxCheckCommand>("test.commands.inbox-check", 1);
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
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        await scheduler.AddAsync(new InboxCheckCommand());
        await processor.ProcessPendingAsync();

        capture.IsInboxExecution.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldPropagateTraceMetadataToExecutionContext()
    {
        var store = new InMemoryCommandInboxStore();
        var capture = new TraceMetadataCapture();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(store)
            .AddSingleton<IInboxStateStore>(store)
            .AddSingleton(capture)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<InboxCheckCommand>();
                    builder.Register<TraceMetadataCommandHandler>();
                });

                configuration.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxCheckCommand>("test.commands.inbox-check", 1);
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
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        await scheduler.AddAsync(new InboxCheckCommand(), new InboxOptions
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
        var store = new InMemoryCommandInboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<IInboxStore>(store)
            .AddSingleton<IInboxLeaseStore>(store)
            .AddSingleton<IInboxStateStore>(store)
            .AddCommandMediatorInboxDispatcher()
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<FaultyCommand>();
                    builder.Register<FaultyCommandHandler>();
                });

                configuration.AddInboxModule(builder =>
                {
                    builder.Contracts.Register<FaultyCommand>("orders.commands.faulty", 1);
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
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.AddAsync(new FaultyCommand());
        await processor.ProcessPendingAsync();

        var lastError = store.Get(receipt.Id).LastError;
        lastError.Should().Be($"{typeof(InvalidOperationException).FullName}: Simulated handler failure.");
        lastError.Should().NotContain(" at ");
    }

    [Fact]
    public void InboxProcessor_WithInvalidMaxAttempts_ShouldThrow()
    {
        var act = () => new InboxProcessor(
            new InMemoryCommandInboxStore(),
            new InMemoryCommandInboxStore(),
            new StubInboxDispatcher(),
            new InboxProcessorOptions
            {
                Retry = new RetryOptions { MaxAttempts = 0 }
            },
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    internal sealed class StubInboxDispatcher : IInboxDispatcher
    {
        public Task DispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
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

    public sealed record ShipOrderCommand : ICommand
    {
        public Guid OrderId { get; init; }

        public required string IdempotencyKey { get; init; }
    }

    public sealed class ShipOrderCommandHandler : ICommandHandler<ShipOrderCommand>
    {
        private readonly CommandRecorder _recorder;

        public ShipOrderCommandHandler(CommandRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(ShipOrderCommand message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message);
            return Task.CompletedTask;
        }
    }

    public sealed class CommandRecorder
    {
        private readonly List<ShipOrderCommand> _commands = [];

        public IReadOnlyList<ShipOrderCommand> Commands => _commands;

        public void Record(ShipOrderCommand command)
        {
            _commands.Add(command);
        }
    }

    public sealed record ArchiveCommand<T> : ICommand
    {
        public required T Value { get; init; }
    }

    public sealed class ArchiveStringCommandHandler : ICommandHandler<ArchiveCommand<string>>
    {
        private readonly GenericCommandRecorder _recorder;

        public ArchiveStringCommandHandler(GenericCommandRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task HandleAsync(ArchiveCommand<string> message, CancellationToken cancellationToken = default)
        {
            _recorder.Record(message.Value);
            return Task.CompletedTask;
        }
    }

    public sealed class GenericCommandRecorder
    {
        private readonly List<string> _values = [];

        public IReadOnlyList<string> Values => _values;

        public void Record(string value)
        {
            _values.Add(value);
        }
    }

    public sealed record GetOrderStatusCommand : ICommand<string>
    {
        public Guid OrderId { get; init; }
    }

    public sealed record FaultyCommand : ICommand;

    public sealed class FaultyCommandHandler : ICommandHandler<FaultyCommand>
    {
        public Task HandleAsync(FaultyCommand message, CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("Simulated handler failure.");
        }
    }

    public sealed class IsInboxCapture
    {
        public bool IsInboxExecution { get; set; }
    }

    public sealed class TraceMetadataCapture
    {
        public string? CorrelationId { get; set; }

        public string? CausationId { get; set; }

        public string? TenantId { get; set; }
    }

    public sealed record InboxCheckCommand : ICommand;

    public sealed class InboxCheckCommandHandler : ICommandHandler<InboxCheckCommand>
    {
        private readonly IsInboxCapture _capture;

        public InboxCheckCommandHandler(IsInboxCapture capture)
        {
            _capture = capture;
        }

        public Task HandleAsync(InboxCheckCommand message, CancellationToken cancellationToken = default)
        {
            _capture.IsInboxExecution =
                AmbientExecutionContext.Current.Items.TryGetValue(
                    InboxExecutionContextKeys.IsInboxExecution, out var value) &&
                value is true;

            return Task.CompletedTask;
        }
    }

    public sealed class TraceMetadataCommandHandler : ICommandHandler<InboxCheckCommand>
    {
        private readonly TraceMetadataCapture _capture;

        public TraceMetadataCommandHandler(TraceMetadataCapture capture)
        {
            _capture = capture;
        }

        public Task HandleAsync(InboxCheckCommand message, CancellationToken cancellationToken = default)
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

    public sealed class InMemoryCommandInboxStore : IInboxStore, IInboxLeaseStore, IInboxStateStore
    {
        private readonly Dictionary<Guid, InboxEnvelope> _envelopes = [];

        public Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.IdempotencyKey is not null)
            {
                var existing = _envelopes.Values.SingleOrDefault(value => value.IdempotencyKey == envelope.IdempotencyKey);

                if (existing is not null)
                {
                    return Task.FromResult(existing);
                }
            }

            _envelopes[envelope.Id] = envelope;
            return Task.FromResult(envelope);
        }

        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(InboxLeaseRequest request, CancellationToken cancellationToken = default)
        {
            var leased = _envelopes.Values
                .Where(envelope => IsAvailable(envelope, request.Now))
                .OrderBy(envelope => envelope.CreatedAt)
                .Take(request.BatchSize)
                .Select(envelope => envelope with
                {
                    Status = InboxStatus.Processing,
                    LeaseOwner = request.LeaseOwner,
                    LeaseExpiresAt = request.Now.Add(request.LeaseDuration),
                    AttemptCount = envelope.AttemptCount + 1
                })
                .ToArray();

            foreach (var envelope in leased)
            {
                _envelopes[envelope.Id] = envelope;
            }

            return Task.FromResult<IReadOnlyList<InboxEnvelope>>(leased);
        }

        public Task MarkCompletedAsync(Guid commandId, CancellationToken cancellationToken = default)
        {
            var envelope = Get(commandId);
            _envelopes[commandId] = envelope with
            {
                Status = InboxStatus.Completed,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = null
            };

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(InboxEnvelopeFailure failure, CancellationToken cancellationToken = default)
        {
            var envelope = Get(failure.Id);
            _envelopes[failure.Id] = envelope with
            {
                Status = InboxStatus.Failed,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = failure.Error,
                VisibleAfter = failure.VisibleAfter
            };

            return Task.CompletedTask;
        }

        public Task MoveToDeadLetterAsync(InboxEnvelopeDeadLetter deadLetter, CancellationToken cancellationToken = default)
        {
            var envelope = Get(deadLetter.Id);
            _envelopes[deadLetter.Id] = envelope with
            {
                Status = InboxStatus.DeadLettered,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = deadLetter.Reason
            };

            return Task.CompletedTask;
        }

        public InboxEnvelope Get(Guid commandId)
        {
            return _envelopes[commandId];
        }

        public IReadOnlyList<InboxEnvelope> GetAll()
        {
            return _envelopes.Values.ToList();
        }

        private static bool IsAvailable(InboxEnvelope envelope, DateTimeOffset now)
        {
            return ((envelope.Status is InboxStatus.Pending or InboxStatus.Failed) &&
                    (envelope.VisibleAfter is null || envelope.VisibleAfter <= now)) ||
                   (envelope.Status == InboxStatus.Processing && envelope.LeaseExpiresAt <= now);
        }
    }
}
