using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

using CommandInboxProcessorContract = LiteBus.Inbox.Abstractions.ICommandInboxProcessor;

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

        var scheduler = new CommandScheduler(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            new FixedTimeProvider(now));

        var commandId = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var receipt = await scheduler.ScheduleAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        }, new CommandScheduleOptions
        {
            CommandId = commandId,
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            TenantId = "tenant-1"
        });

        receipt.CommandId.Should().Be(commandId);
        receipt.CommandType.Should().Be(typeof(ShipOrderCommand));
        receipt.ContractName.Should().Be("orders.commands.ship");
        receipt.ContractVersion.Should().Be(2);
        receipt.AcceptedAt.Should().Be(now);

        var envelope = store.Get(commandId);
        envelope.ContractName.Should().Be("orders.commands.ship");
        envelope.ContractVersion.Should().Be(2);
        envelope.Status.Should().Be(InboxCommandStatus.Pending);
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
            .AddSingleton<ICommandInboxWriter>(store)
            .AddSingleton<ICommandInboxLeaseStore>(store)
            .AddSingleton<ICommandInboxStateStore>(store)
            .AddSingleton(recorder)
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<ShipOrderCommand>();
                    builder.Register<ShipOrderCommandHandler>();
                });

                configuration.AddCommandInboxModule(builder =>
                {
                    builder.Contracts.Register<ShipOrderCommand>("orders.commands.ship", 1);
                    builder.UseProcessorOptions(new CommandInboxProcessorOptions
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

        var scheduler = serviceProvider.GetRequiredService<ICommandScheduler>();
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();
        var orderId = Guid.NewGuid();
        var receipt = await scheduler.ScheduleAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = $"ship:{orderId}"
        });

        await processor.ProcessPendingAsync();

        recorder.Commands.Should().ContainSingle(command => command.OrderId == orderId);

        var envelope = store.Get(receipt.CommandId);
        envelope.Status.Should().Be(InboxCommandStatus.Completed);
        envelope.AttemptCount.Should().Be(1);
        envelope.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldSupportClosedGenericCommands()
    {
        var store = new InMemoryCommandInboxStore();
        var recorder = new GenericCommandRecorder();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<ICommandInboxWriter>(store)
            .AddSingleton<ICommandInboxLeaseStore>(store)
            .AddSingleton<ICommandInboxStateStore>(store)
            .AddSingleton(recorder)
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<ArchiveCommand<string>>();
                    builder.Register<ArchiveStringCommandHandler>();
                });

                configuration.AddCommandInboxModule(builder =>
                {
                    builder.Contracts.Register<ArchiveCommand<string>>("archive.commands.string", 1);
                    builder.UseProcessorOptions(new CommandInboxProcessorOptions
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

        var scheduler = serviceProvider.GetRequiredService<ICommandScheduler>();
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.ScheduleAsync(new ArchiveCommand<string>
        {
            Value = "closed-generic"
        });

        await processor.ProcessPendingAsync();

        recorder.Values.Should().ContainSingle(value => value == "closed-generic");
        store.Get(receipt.CommandId).Status.Should().Be(InboxCommandStatus.Completed);
    }

    [Fact]
    public async Task ScheduleAsync_WhenCommandImplementsICommandTResult_ShouldThrowArgumentException()
    {
        var store = new InMemoryCommandInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<GetOrderStatusCommand>("orders.commands.get-status", 1);

        var scheduler = new CommandScheduler(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var act = async () => await scheduler.ScheduleAsync(new GetOrderStatusCommand { OrderId = Guid.NewGuid() });

        await act.Should().ThrowAsync<ArgumentException>()
            .WithMessage("*ICommand<TResult>*");
    }

    [Fact]
    public async Task ScheduleAsync_WhenIdempotencyKeyMatchesExisting_ShouldReturnExistingReceipt()
    {
        var store = new InMemoryCommandInboxStore();
        var contractRegistry = new MessageContractRegistry();
        contractRegistry.Register<ShipOrderCommand>("orders.commands.ship", 1);

        var scheduler = new CommandScheduler(
            store,
            contractRegistry,
            new SystemTextJsonMessageSerializer(),
            TimeProvider.System);

        var orderId = Guid.NewGuid();
        var idempotencyKey = $"ship:{orderId}";

        var first = await scheduler.ScheduleAsync(new ShipOrderCommand
        {
            OrderId = orderId,
            IdempotencyKey = idempotencyKey
        });

        var second = await scheduler.ScheduleAsync(new ShipOrderCommand
        {
            OrderId = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey
        });

        second.CommandId.Should().Be(first.CommandId);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerThrows_ShouldMarkFailedAndSetVisibleAfter()
    {
        var store = new InMemoryCommandInboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<ICommandInboxWriter>(store)
            .AddSingleton<ICommandInboxLeaseStore>(store)
            .AddSingleton<ICommandInboxStateStore>(store)
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<FaultyCommand>();
                    builder.Register<FaultyCommandHandler>();
                });

                configuration.AddCommandInboxModule(builder =>
                {
                    builder.Contracts.Register<FaultyCommand>("orders.commands.faulty", 1);
                    builder.UseProcessorOptions(new CommandInboxProcessorOptions
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

        var scheduler = serviceProvider.GetRequiredService<ICommandScheduler>();
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.ScheduleAsync(new FaultyCommand());

        await processor.ProcessPendingAsync();

        var envelope = store.Get(receipt.CommandId);
        envelope.Status.Should().Be(InboxCommandStatus.Failed);
        envelope.LastError.Should().NotBeNullOrWhiteSpace();
        envelope.AttemptCount.Should().Be(1);
    }

    [Fact]
    public async Task ProcessPendingAsync_WhenHandlerExceedsMaxAttempts_ShouldMoveToDeadLetter()
    {
        var store = new InMemoryCommandInboxStore();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<ICommandInboxWriter>(store)
            .AddSingleton<ICommandInboxLeaseStore>(store)
            .AddSingleton<ICommandInboxStateStore>(store)
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<FaultyCommand>();
                    builder.Register<FaultyCommandHandler>();
                });

                configuration.AddCommandInboxModule(builder =>
                {
                    builder.Contracts.Register<FaultyCommand>("orders.commands.faulty", 1);
                    builder.UseProcessorOptions(new CommandInboxProcessorOptions
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

        var scheduler = serviceProvider.GetRequiredService<ICommandScheduler>();
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.ScheduleAsync(new FaultyCommand());

        // Attempt 1 of 2: AttemptCount reaches 1 which is < MaxAttempts (2), so envelope is retried.
        await processor.ProcessPendingAsync();
        // Attempt 2 of 2: AttemptCount reaches 2 which is >= MaxAttempts (2), so envelope is dead-lettered.
        await processor.ProcessPendingAsync();

        store.Get(receipt.CommandId).Status.Should().Be(InboxCommandStatus.DeadLettered);
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldSetIsInboxExecutionContextKey()
    {
        var store = new InMemoryCommandInboxStore();
        var capture = new IsInboxCapture();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<ICommandInboxWriter>(store)
            .AddSingleton<ICommandInboxLeaseStore>(store)
            .AddSingleton<ICommandInboxStateStore>(store)
            .AddSingleton(capture)
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<InboxCheckCommand>();
                    builder.Register<InboxCheckCommandHandler>();
                });

                configuration.AddCommandInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxCheckCommand>("test.commands.inbox-check", 1);
                    builder.UseProcessorOptions(new CommandInboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });
            })
            .BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredService<ICommandScheduler>();
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        await scheduler.ScheduleAsync(new InboxCheckCommand());
        await processor.ProcessPendingAsync();

        capture.IsInboxExecution.Should().BeTrue();
    }

    [Fact]
    public async Task ProcessPendingAsync_ShouldPropagateTraceMetadataToExecutionContext()
    {
        var store = new InMemoryCommandInboxStore();
        var capture = new TraceMetadataCapture();

        var serviceProvider = new ServiceCollection()
            .AddSingleton<ICommandInboxWriter>(store)
            .AddSingleton<ICommandInboxLeaseStore>(store)
            .AddSingleton<ICommandInboxStateStore>(store)
            .AddSingleton(capture)
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<InboxCheckCommand>();
                    builder.Register<TraceMetadataCommandHandler>();
                });

                configuration.AddCommandInboxModule(builder =>
                {
                    builder.Contracts.Register<InboxCheckCommand>("test.commands.inbox-check", 1);
                    builder.UseProcessorOptions(new CommandInboxProcessorOptions
                    {
                        BatchSize = 10,
                        LeaseOwner = "test-worker",
                        Retry = new RetryOptions { UseJitter = false }
                    });
                });
            })
            .BuildServiceProvider();

        var scheduler = serviceProvider.GetRequiredService<ICommandScheduler>();
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        await scheduler.ScheduleAsync(new InboxCheckCommand(), new CommandScheduleOptions
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
            .AddSingleton<ICommandInboxWriter>(store)
            .AddSingleton<ICommandInboxLeaseStore>(store)
            .AddSingleton<ICommandInboxStateStore>(store)
            .AddLiteBus(configuration =>
            {
                configuration.AddCommandModule(builder =>
                {
                    builder.Register<FaultyCommand>();
                    builder.Register<FaultyCommandHandler>();
                });

                configuration.AddCommandInboxModule(builder =>
                {
                    builder.Contracts.Register<FaultyCommand>("orders.commands.faulty", 1);
                    builder.UseProcessorOptions(new CommandInboxProcessorOptions
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

        var scheduler = serviceProvider.GetRequiredService<ICommandScheduler>();
        var processor = serviceProvider.GetRequiredService<CommandInboxProcessorContract>();

        var receipt = await scheduler.ScheduleAsync(new FaultyCommand());
        await processor.ProcessPendingAsync();

        var lastError = store.Get(receipt.CommandId).LastError;
        lastError.Should().Be($"{typeof(InvalidOperationException).FullName}: Simulated handler failure.");
        lastError.Should().NotContain(" at ");
    }

    [Fact]
    public void CommandInboxProcessor_WithInvalidMaxAttempts_ShouldThrow()
    {
        var act = () => new CommandInboxProcessor(
            new InMemoryCommandInboxStore(),
            new InMemoryCommandInboxStore(),
            new StubCommandMediator(),
            new MessageContractRegistry(),
            new SystemTextJsonMessageSerializer(),
            new CommandInboxProcessorOptions
            {
                Retry = new RetryOptions { MaxAttempts = 0 }
            },
            TimeProvider.System);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private sealed class StubCommandMediator : ICommandMediator
    {
        public Task SendAsync(ICommand command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(default(TCommandResult)!);
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

    public sealed record ShipOrderCommand : IIdempotentCommand
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
                    CommandInboxExecutionContextKeys.IsInboxExecution, out var value) &&
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

    internal sealed class InMemoryCommandInboxStore : ICommandInboxWriter, ICommandInboxLeaseStore, ICommandInboxStateStore
    {
        private readonly Dictionary<Guid, InboxCommandEnvelope> _envelopes = [];

        public Task<InboxCommandEnvelope> AddAsync(InboxCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            if (envelope.IdempotencyKey is not null)
            {
                var existing = _envelopes.Values.SingleOrDefault(value => value.IdempotencyKey == envelope.IdempotencyKey);

                if (existing is not null)
                {
                    return Task.FromResult(existing);
                }
            }

            _envelopes[envelope.CommandId] = envelope;
            return Task.FromResult(envelope);
        }

        public Task<IReadOnlyList<InboxCommandEnvelope>> LeasePendingAsync(InboxLeaseRequest request, CancellationToken cancellationToken = default)
        {
            var leased = _envelopes.Values
                .Where(envelope => IsAvailable(envelope, request.Now))
                .OrderBy(envelope => envelope.CreatedAt)
                .Take(request.BatchSize)
                .Select(envelope => envelope with
                {
                    Status = InboxCommandStatus.Processing,
                    LeaseOwner = request.LeaseOwner,
                    LeaseExpiresAt = request.Now.Add(request.LeaseDuration),
                    AttemptCount = envelope.AttemptCount + 1
                })
                .ToArray();

            foreach (var envelope in leased)
            {
                _envelopes[envelope.CommandId] = envelope;
            }

            return Task.FromResult<IReadOnlyList<InboxCommandEnvelope>>(leased);
        }

        public Task MarkCompletedAsync(Guid commandId, CancellationToken cancellationToken = default)
        {
            var envelope = Get(commandId);
            _envelopes[commandId] = envelope with
            {
                Status = InboxCommandStatus.Completed,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = null
            };

            return Task.CompletedTask;
        }

        public Task MarkFailedAsync(InboxCommandFailure failure, CancellationToken cancellationToken = default)
        {
            var envelope = Get(failure.CommandId);
            _envelopes[failure.CommandId] = envelope with
            {
                Status = InboxCommandStatus.Failed,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = failure.Error,
                VisibleAfter = failure.VisibleAfter
            };

            return Task.CompletedTask;
        }

        public Task MoveToDeadLetterAsync(InboxCommandDeadLetter deadLetter, CancellationToken cancellationToken = default)
        {
            var envelope = Get(deadLetter.CommandId);
            _envelopes[deadLetter.CommandId] = envelope with
            {
                Status = InboxCommandStatus.DeadLettered,
                LeaseOwner = null,
                LeaseExpiresAt = null,
                LastError = deadLetter.Reason
            };

            return Task.CompletedTask;
        }

        public InboxCommandEnvelope Get(Guid commandId)
        {
            return _envelopes[commandId];
        }

        private static bool IsAvailable(InboxCommandEnvelope envelope, DateTimeOffset now)
        {
            return ((envelope.Status is InboxCommandStatus.Pending or InboxCommandStatus.Failed) &&
                    (envelope.VisibleAfter is null || envelope.VisibleAfter <= now)) ||
                   (envelope.Status == InboxCommandStatus.Processing && envelope.LeaseExpiresAt <= now);
        }
    }
}
