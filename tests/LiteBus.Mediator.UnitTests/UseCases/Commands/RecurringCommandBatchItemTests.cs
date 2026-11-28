using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Covers per-item recurring batches: per-item identities, that one failing item never stops the rest, that
///     refused items are isolated and back the loop off, and the batch factory helpers.
/// </summary>
public sealed class RecurringCommandBatchItemTests
{
    [Fact]
    public void From_plain_commands_assigns_no_identity()
    {
        var first = new BatchItemCommand { Name = "first" };
        var last = new BatchItemCommand { Name = "last" };

        var batch = RecurringCommandBatch.From([first, last]);

        batch.Items.Should().HaveCount(2);
        batch.Items[0].Command.Should().BeSameAs(first);
        batch.Items[0].Identity.Should().BeNull();
        batch.Items[1].Command.Should().BeSameAs(last);
        batch.Items[1].Identity.Should().BeNull();
        batch.HasMoreWork.Should().BeFalse();
        batch.Isolation.Should().Be(RecurringBatchIsolation.Atomic);
    }

    [Fact]
    public void From_items_preserves_commands_and_identities()
    {
        var first = new BatchItemCommand { Name = "first" };
        var last = new BatchItemCommand { Name = "last" };
        var identity = ExecutionIdentity.ForTenant("tenant-a");

        var batch = RecurringCommandBatch.From(
        [
            new RecurringCommandItem(first, identity),
            new RecurringCommandItem(last),
        ]);

        batch.Items.Should().HaveCount(2);
        batch.Items[0].Command.Should().BeSameAs(first);
        batch.Items[0].Identity.Should().Be(identity);
        batch.Items[1].Command.Should().BeSameAs(last);
        batch.Items[1].Identity.Should().BeNull();
        batch.Isolation.Should().Be(RecurringBatchIsolation.Atomic);
    }

    [Fact]
    public void Empty_has_no_items()
    {
        RecurringCommandBatch.Empty.Items.Should().BeEmpty();
        RecurringCommandBatch.Empty.HasMoreWork.Should().BeFalse();
        RecurringCommandBatch.Empty.Isolation.Should().Be(RecurringBatchIsolation.Atomic);
    }

    [Fact]
    public async Task Each_item_sends_with_its_own_identity()
    {
        var log = new BatchLog();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands.AddRecurringBatch<IdentityBatchSource>(
                host => host.Interval = TimeSpan.FromMilliseconds(5)));

        var service = serviceProvider.GetRequiredService<RecurringCommandBatchBackgroundService<IdentityBatchSource>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Should().HaveCountGreaterThanOrEqualTo(2);
        log.Attempts.Take(2).Should().Equal(new BatchAttempt("first", "tenant-a"), new BatchAttempt("last", "tenant-b"));
    }

    [Fact]
    public async Task One_failing_item_does_not_stop_the_rest()
    {
        var log = new BatchLog();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands.AddRecurringBatch<FailingBatchSource>(
                host =>
                {
                    host.Interval = TimeSpan.FromMilliseconds(5);
                    host.ErrorBackoff = TimeSpan.FromMilliseconds(5);
                }));

        var service = serviceProvider.GetRequiredService<RecurringCommandBatchBackgroundService<FailingBatchSource>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);

        // The first pass attempts every item in order even though the middle one throws.
        await log.WaitForAttemptsAsync(3).ConfigureAwait(false);
        log.Attempts.Take(3).Select(attempt => attempt.Name).Should().Equal("first", "failing", "last");

        // The loop survives the failing pass and sends the batch again.
        await log.WaitForAttemptsAsync(6).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Should().HaveCountGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task Refused_items_are_isolated_logged_and_back_the_loop_off()
    {
        var log = new BatchLog();
        var logger = new RecordingLogger<RecurringCommandBatchBackgroundService<RefusedBatchSource>>();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands
                .Register<DenyingBatchGuard>()
                .Register<RejectingBatchValidator>()
                .AddRecurringBatch<RefusedBatchSource>(
                    host =>
                    {
                        host.Interval = TimeSpan.FromMilliseconds(5);
                        host.ErrorBackoff = TimeSpan.FromHours(1);
                    }),
            services => services.AddSingleton<ILogger<RecurringCommandBatchBackgroundService<RefusedBatchSource>>>(logger));

        var service = serviceProvider.GetRequiredService<RecurringCommandBatchBackgroundService<RefusedBatchSource>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(2).ConfigureAwait(false);

        // Only the two accepted items ran; the denied and invalid commands never reached their handlers.
        log.Attempts.Select(attempt => attempt.Name).Should().Equal("first", "last");

        var refusals = logger.Entries
            .Where(entry => entry.Level == LogLevel.Warning && entry.Id.Id == 2113)
            .ToList();
        refusals.Should().HaveCount(2);
        refusals[0].Message.Should().Contain("index 1").And.Contain("BATCH_DENIED");
        refusals[1].Message.Should().Contain("index 2").And.Contain("BATCH_INVALID");

        // The failing pass backs off instead of pacing at the interval: no second pass runs.
        await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
        log.Attempts.Should().HaveCount(2);

        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
    }

    private static ServiceProvider BuildProvider(
        BatchLog log,
        Action<CommandModuleBuilder> register,
        Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        extra?.Invoke(services);

        return services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<BatchItemCommandHandler>();
                commands.Register<DeniedBatchCommandHandler>();
                commands.Register<InvalidBatchCommandHandler>();
                register(commands);
            });
        }).BuildServiceProvider();
    }

    /// <summary>
    ///     Records the batch attempts handlers observe and lets a test wait for a number of them.
    /// </summary>
    public sealed class BatchLog : IDisposable
    {
        /// <summary>
        ///     Signals each time an attempt completes so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The attempts observed so far, in the order they were recorded.
        /// </summary>
        private readonly List<BatchAttempt> _attempts = [];

        /// <summary>
        ///     Gets the attempts observed so far, in the order they were recorded.
        /// </summary>
        public IReadOnlyList<BatchAttempt> Attempts
        {
            get
            {
                lock (_attempts)
                {
                    return [.. _attempts];
                }
            }
        }

        /// <summary>
        ///     Records one attempt.
        /// </summary>
        /// <param name="attempt">The observed attempt.</param>
        public void Record(BatchAttempt attempt)
        {
            lock (_attempts)
            {
                _attempts.Add(attempt);
            }

            _signal.Release();
        }

        /// <summary>
        ///     Waits until at least <paramref name="target" /> attempts have been recorded.
        /// </summary>
        /// <param name="target">The number of attempts to wait for.</param>
        /// <returns>A task that completes once the count is reached.</returns>
        public async Task WaitForAttemptsAsync(int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (Attempts.Count < target)
            {
                await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Releases the signal used to wait for attempts.
        /// </summary>
        public void Dispose()
        {
            _signal.Dispose();
        }
    }

    /// <summary>
    ///     One batch attempt: the command name and the tenant the handler observed.
    /// </summary>
    public sealed record BatchAttempt(string Name, string? TenantId);

    /// <summary>
    ///     Captures log entries so a test can assert the refusal the loop logged.
    /// </summary>
    /// <typeparam name="T">The logged category type.</typeparam>
    public sealed class RecordingLogger<T> : ILogger<T>
    {
        /// <summary>
        ///     The entries logged so far, in the order they were written.
        /// </summary>
        private readonly List<RecordingLogEntry> _entries = [];

        /// <summary>
        ///     Gets the entries logged so far, in the order they were written.
        /// </summary>
        public IReadOnlyList<RecordingLogEntry> Entries
        {
            get
            {
                lock (_entries)
                {
                    return [.. _entries];
                }
            }
        }

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull
        {
            return new NullScope();
        }

        /// <summary>
        ///     Determines whether the logger records the supplied level.
        /// </summary>
        /// <param name="logLevel">The level to check.</param>
        /// <returns>Always <see langword="true" />.</returns>
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        /// <summary>
        ///     Records one log entry.
        /// </summary>
        /// <typeparam name="TState">The type of the logged state.</typeparam>
        /// <param name="logLevel">The level the entry was logged at.</param>
        /// <param name="eventId">The event identifier.</param>
        /// <param name="state">The logged state.</param>
        /// <param name="exception">The logged exception, when any.</param>
        /// <param name="formatter">Renders the state and the exception as a message.</param>
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_entries)
            {
                _entries.Add(new RecordingLogEntry(logLevel, eventId, formatter(state, exception)));
            }
        }

        /// <summary>
        ///     A scope that releases nothing.
        /// </summary>
        private sealed class NullScope : IDisposable
        {
            public void Dispose()
            {
            }
        }
    }

    /// <summary>
    ///     One captured log entry.
    /// </summary>
    public sealed record RecordingLogEntry(LogLevel Level, EventId Id, string Message);

    /// <summary>
    ///     A batch command that optionally fails when handled.
    /// </summary>
    public sealed class BatchItemCommand : ICommand
    {
        public string Name { get; init; } = "";

        public bool Fail { get; init; }
    }

    /// <summary>
    ///     A batch command the test guard denies.
    /// </summary>
    public sealed class DeniedBatchCommand : ICommand;

    /// <summary>
    ///     A batch command the test validator rejects.
    /// </summary>
    public sealed class InvalidBatchCommand : ICommand;

    private sealed class BatchItemCommandHandler : ICommandHandler<BatchItemCommand>
    {
        private readonly BatchLog _log;

        public BatchItemCommandHandler(BatchLog log)
        {
            _log = log;
        }

        public Task HandleAsync(BatchItemCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(new BatchAttempt(message.Name, AmbientExecutionContext.Current.Identity.TenantId));

            if (message.Fail)
            {
                throw new InvalidOperationException($"item '{message.Name}' failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DeniedBatchCommandHandler : ICommandHandler<DeniedBatchCommand>
    {
        private readonly BatchLog _log;

        public DeniedBatchCommandHandler(BatchLog log)
        {
            _log = log;
        }

        public Task HandleAsync(DeniedBatchCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(new BatchAttempt("denied", AmbientExecutionContext.Current.Identity.TenantId));
            return Task.CompletedTask;
        }
    }

    private sealed class InvalidBatchCommandHandler : ICommandHandler<InvalidBatchCommand>
    {
        private readonly BatchLog _log;

        public InvalidBatchCommandHandler(BatchLog log)
        {
            _log = log;
        }

        public Task HandleAsync(InvalidBatchCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(new BatchAttempt("invalid", AmbientExecutionContext.Current.Identity.TenantId));
            return Task.CompletedTask;
        }
    }

    private sealed class DenyingBatchGuard : ICommandGuard<DeniedBatchCommand>
    {
        public Task<Verdict> DecideAsync(DeniedBatchCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Verdict.Deny("the batch test denies this command", "BATCH_DENIED"));
        }
    }

    private sealed class RejectingBatchValidator : ICommandValidator<InvalidBatchCommand>
    {
        public Task<Validity> ValidateAsync(InvalidBatchCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Validity.Invalid("the batch test rejects this command", "Name", "BATCH_INVALID"));
        }
    }

    private sealed class IdentityBatchSource : IRecurringCommandBatchSource
    {
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(RecurringCommandBatch.From(
            [
                new RecurringCommandItem(
                    new BatchItemCommand { Name = "first" },
                    ExecutionIdentity.ForTenant("tenant-a")),
                new RecurringCommandItem(
                    new BatchItemCommand { Name = "last" },
                    ExecutionIdentity.ForTenant("tenant-b")),
            ],
            isolation: RecurringBatchIsolation.PerItem));
        }
    }

    private sealed class FailingBatchSource : IRecurringCommandBatchSource
    {
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(RecurringCommandBatch.From(
            [
                new BatchItemCommand { Name = "first" },
                new BatchItemCommand { Name = "failing", Fail = true },
                new BatchItemCommand { Name = "last" },
            ]) with
            {
                Isolation = RecurringBatchIsolation.PerItem
            });
        }
    }

    private sealed class RefusedBatchSource : IRecurringCommandBatchSource
    {
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(RecurringCommandBatch.From(
            [
                new BatchItemCommand { Name = "first" },
                new DeniedBatchCommand(),
                new InvalidBatchCommand(),
                new BatchItemCommand { Name = "last" },
            ]) with
            {
                Isolation = RecurringBatchIsolation.PerItem
            });
        }
    }
}
