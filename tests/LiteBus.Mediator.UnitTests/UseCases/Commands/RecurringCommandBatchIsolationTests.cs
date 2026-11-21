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
///     Covers recurring batch isolation: an atomic batch commits or rolls back as one, an atomic batch that mixes
///     identities fails the pass, and a per-item batch sends around a failing item with each identity.
/// </summary>
public sealed class RecurringCommandBatchIsolationTests
{
    [Fact]
    public void From_items_with_an_explicit_isolation_preserves_it()
    {
        var command = new IsolationBatchCommand { Name = "first" };

        var batch = RecurringCommandBatch.From(
            [new RecurringCommandItem(command)],
            isolation: RecurringBatchIsolation.PerItem);

        batch.Isolation.Should().Be(RecurringBatchIsolation.PerItem);
        batch.Items.Should().HaveCount(1);
        batch.Items[0].Command.Should().BeSameAs(command);
    }

    [Fact]
    public async Task Atomic_batch_with_a_refused_command_rolls_back_every_command()
    {
        var log = new IsolationLog();
        var units = new IsolationUnitOfWorkRecorder();
        var logger = new IsolationRecordingLogger<RecurringCommandBatchBackgroundService<AtomicRefusedBatchSource>>();
        await using var serviceProvider = BuildProvider(
            log,
            units,
            commands => commands
                .Register<DenyingIsolationGuard>()
                .AddRecurringBatch<AtomicRefusedBatchSource>(
                    host =>
                    {
                        host.Interval = TimeSpan.FromMilliseconds(5);
                        host.ErrorBackoff = TimeSpan.FromHours(1);
                    }),
            services => services.AddSingleton<ILogger<RecurringCommandBatchBackgroundService<AtomicRefusedBatchSource>>>(logger));

        var service = serviceProvider.GetRequiredService<RecurringCommandBatchBackgroundService<AtomicRefusedBatchSource>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        var refusal = await logger
            .WaitForEntryAsync(entry => entry.Level == LogLevel.Warning && entry.Id.Id == 2107)
            .ConfigureAwait(false);

        refusal.Message.Should().Contain("rolled back")
            .And.Contain("index 1")
            .And.Contain(nameof(DeniedIsolationCommand))
            .And.Contain(DenyingIsolationGuard.RefusalCode);

        // The batch stopped at the refused command: the command after it never ran, and nothing committed.
        log.Attempts.Select(attempt => attempt.Name).Should().Equal("first");
        units.Begins.Should().Be(1);
        units.Commits.Should().Be(0);
        units.Rollbacks.Should().Be(1);

        // The refused pass backs off instead of pacing at the interval: no second pass runs.
        await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
        log.Attempts.Should().HaveCount(1);
        units.Rollbacks.Should().Be(1);

        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
    }

    [Fact]
    public async Task Atomic_batch_with_mixed_identities_fails_the_pass()
    {
        var log = new IsolationLog();
        var logger = new IsolationRecordingLogger<RecurringCommandBatchBackgroundService<MixedIdentityBatchSource>>();
        await using var serviceProvider = BuildProvider(
            log,
            null,
            commands => commands.AddRecurringBatch<MixedIdentityBatchSource>(
                host =>
                {
                    host.Interval = TimeSpan.FromMilliseconds(5);
                    host.ErrorBackoff = TimeSpan.FromHours(1);
                }),
            services => services.AddSingleton<ILogger<RecurringCommandBatchBackgroundService<MixedIdentityBatchSource>>>(logger));

        var service = serviceProvider.GetRequiredService<RecurringCommandBatchBackgroundService<MixedIdentityBatchSource>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        var failure = await logger
            .WaitForEntryAsync(entry => entry.Level == LogLevel.Error && entry.Id.Id == 2103)
            .ConfigureAwait(false);

        failure.Exception.Should().BeOfType<InvalidOperationException>()
            .Which.Message.Should().Contain("PerItem");

        // The pass failed before sending, so no command ran.
        log.Attempts.Should().BeEmpty();

        // The failing pass backs off instead of pacing at the interval: no second pass runs.
        await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
        log.Attempts.Should().BeEmpty();
        logger.Entries.Count(entry => entry.Level == LogLevel.Error && entry.Id.Id == 2103).Should().Be(1);

        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
    }

    [Fact]
    public async Task Per_item_batch_with_a_failing_item_still_sends_the_rest_with_their_identities()
    {
        var log = new IsolationLog();
        var logger = new IsolationRecordingLogger<RecurringCommandBatchBackgroundService<PerItemFailingBatchSource>>();
        await using var serviceProvider = BuildProvider(
            log,
            null,
            commands => commands.AddRecurringBatch<PerItemFailingBatchSource>(
                host =>
                {
                    host.Interval = TimeSpan.FromMilliseconds(5);
                    host.ErrorBackoff = TimeSpan.FromMilliseconds(5);
                }),
            services => services.AddSingleton<ILogger<RecurringCommandBatchBackgroundService<PerItemFailingBatchSource>>>(logger));

        var service = serviceProvider.GetRequiredService<RecurringCommandBatchBackgroundService<PerItemFailingBatchSource>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);

        // The first pass attempts every item in order, each with its own identity, even though the middle one throws.
        await log.WaitForAttemptsAsync(3).ConfigureAwait(false);
        log.Attempts.Take(3).Should().Equal(
            new IsolationAttempt("first", "tenant-a"),
            new IsolationAttempt("failing", "tenant-b"),
            new IsolationAttempt("last", "tenant-c"));

        // The loop survives the failing pass and sends the batch again.
        await log.WaitForAttemptsAsync(6).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Should().HaveCountGreaterThanOrEqualTo(6);
        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Error && entry.Id.Id == 2108
            && entry.Message.Contains("index 1", StringComparison.Ordinal));
        logger.Entries.Should().Contain(entry => entry.Level == LogLevel.Warning && entry.Id.Id == 2109);
    }

    private static ServiceProvider BuildProvider(
        IsolationLog log,
        IsolationUnitOfWorkRecorder? units,
        Action<CommandModuleBuilder> register,
        Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);

        if (units is not null)
        {
            services.AddSingleton(units);
        }

        extra?.Invoke(services);

        return services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<IsolationBatchCommandHandler>();
                commands.Register<DeniedIsolationCommandHandler>();
                register(commands);

                if (units is not null)
                {
                    commands.UseUnitOfWork<RecordingIsolationUnitOfWork>();
                }
            });
        }).BuildServiceProvider();
    }

    /// <summary>
    ///     Records the batch attempts handlers observe and lets a test wait for a number of them.
    /// </summary>
    public sealed class IsolationLog : IDisposable
    {
        /// <summary>
        ///     Signals each time an attempt completes so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The attempts observed so far, in the order they were recorded.
        /// </summary>
        private readonly List<IsolationAttempt> _attempts = [];

        /// <summary>
        ///     Gets the attempts observed so far, in the order they were recorded.
        /// </summary>
        public IReadOnlyList<IsolationAttempt> Attempts
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
        public void Record(IsolationAttempt attempt)
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
    public sealed record IsolationAttempt(string Name, string? TenantId);

    /// <summary>
    ///     Captures log entries so a test can wait for the entry the loop logs.
    /// </summary>
    /// <typeparam name="T">The logged category type.</typeparam>
    public sealed class IsolationRecordingLogger<T> : ILogger<T>, IDisposable
    {
        /// <summary>
        ///     Signals each time an entry is logged so a waiter can re-check the entries.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The entries logged so far, in the order they were written.
        /// </summary>
        private readonly List<IsolationLogEntry> _entries = [];

        /// <summary>
        ///     Gets the entries logged so far, in the order they were written.
        /// </summary>
        public IReadOnlyList<IsolationLogEntry> Entries
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
                _entries.Add(new IsolationLogEntry(logLevel, eventId, formatter(state, exception), exception));
            }

            _signal.Release();
        }

        /// <summary>
        ///     Waits until a logged entry matches <paramref name="match" />.
        /// </summary>
        /// <param name="match">The predicate a matching entry satisfies.</param>
        /// <returns>The first matching entry.</returns>
        public async Task<IsolationLogEntry> WaitForEntryAsync(Func<IsolationLogEntry, bool> match)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (true)
            {
                lock (_entries)
                {
                    var found = _entries.FirstOrDefault(match);

                    if (found is not null)
                    {
                        return found;
                    }
                }

                await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Releases the signal used to wait for entries.
        /// </summary>
        public void Dispose()
        {
            _signal.Dispose();
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
    public sealed record IsolationLogEntry(LogLevel Level, EventId Id, string Message, Exception? Exception);

    /// <summary>
    ///     A batch command that optionally fails when handled.
    /// </summary>
    public sealed class IsolationBatchCommand : ICommand
    {
        public string Name { get; init; } = "";

        public bool Fail { get; init; }
    }

    /// <summary>
    ///     A batch command the test guard denies.
    /// </summary>
    public sealed class DeniedIsolationCommand : ICommand;

    /// <summary>
    ///     Counts unit of work calls during one isolation test run.
    /// </summary>
    public sealed class IsolationUnitOfWorkRecorder
    {
        /// <summary>
        ///     Gets or sets the number of observed transaction opens.
        /// </summary>
        public int Begins { get; set; }

        /// <summary>
        ///     Gets or sets the number of observed commits.
        /// </summary>
        public int Commits { get; set; }

        /// <summary>
        ///     Gets or sets the number of observed rollbacks.
        /// </summary>
        public int Rollbacks { get; set; }
    }

    private sealed class IsolationBatchCommandHandler : ICommandHandler<IsolationBatchCommand>
    {
        private readonly IsolationLog _log;

        public IsolationBatchCommandHandler(IsolationLog log)
        {
            _log = log;
        }

        public Task HandleAsync(IsolationBatchCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(new IsolationAttempt(message.Name, AmbientExecutionContext.Current.Identity.TenantId));

            if (message.Fail)
            {
                throw new InvalidOperationException($"item '{message.Name}' failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DeniedIsolationCommandHandler : ICommandHandler<DeniedIsolationCommand>
    {
        private readonly IsolationLog _log;

        public DeniedIsolationCommandHandler(IsolationLog log)
        {
            _log = log;
        }

        public Task HandleAsync(DeniedIsolationCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(new IsolationAttempt("denied", AmbientExecutionContext.Current.Identity.TenantId));
            return Task.CompletedTask;
        }
    }

    private sealed class DenyingIsolationGuard : ICommandGuard<DeniedIsolationCommand>
    {
        public const string RefusalCode = "ISOLATION_DENIED";

        public Task<Verdict> DecideAsync(DeniedIsolationCommand message, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Verdict.Deny("the isolation test denies this command", RefusalCode));
        }
    }

    private sealed class RecordingIsolationUnitOfWork : IUnitOfWork, IUnitOfWorkBoundary
    {
        private readonly IsolationUnitOfWorkRecorder _recorder;

        public RecordingIsolationUnitOfWork(IsolationUnitOfWorkRecorder recorder)
        {
            _recorder = recorder;
        }

        public Task BeginAsync(CancellationToken cancellationToken = default)
        {
            _recorder.Begins++;
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            _recorder.Commits++;
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            _recorder.Rollbacks++;
            return Task.CompletedTask;
        }
    }

    private sealed class AtomicRefusedBatchSource : IRecurringCommandBatchSource
    {
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(RecurringCommandBatch.From(
            [
                new IsolationBatchCommand { Name = "first" },
                new DeniedIsolationCommand(),
                new IsolationBatchCommand { Name = "last" },
            ]));
        }
    }

    private sealed class MixedIdentityBatchSource : IRecurringCommandBatchSource
    {
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(RecurringCommandBatch.From(
            [
                new RecurringCommandItem(
                    new IsolationBatchCommand { Name = "first" },
                    ExecutionIdentity.ForTenant("tenant-a")),
                new RecurringCommandItem(
                    new IsolationBatchCommand { Name = "last" },
                    ExecutionIdentity.ForTenant("tenant-b")),
            ]));
        }
    }

    private sealed class PerItemFailingBatchSource : IRecurringCommandBatchSource
    {
        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(RecurringCommandBatch.From(
            [
                new RecurringCommandItem(
                    new IsolationBatchCommand { Name = "first" },
                    ExecutionIdentity.ForTenant("tenant-a")),
                new RecurringCommandItem(
                    new IsolationBatchCommand { Name = "failing", Fail = true },
                    ExecutionIdentity.ForTenant("tenant-b")),
                new RecurringCommandItem(
                    new IsolationBatchCommand { Name = "last" },
                    ExecutionIdentity.ForTenant("tenant-c")),
            ],
            isolation: RecurringBatchIsolation.PerItem));
        }
    }
}
