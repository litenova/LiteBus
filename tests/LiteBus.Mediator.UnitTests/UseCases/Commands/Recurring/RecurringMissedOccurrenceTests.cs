using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.Recurring;

/// <summary>
///     Covers the missed-occurrence policy: one catch-up pass inside the window, waiting past it, logging every gap,
///     the in-memory restart behavior, the truncation of outage-scale backlogs, and policy validation.
/// </summary>
public sealed class RecurringMissedOccurrenceTests
{
    [Fact]
    public void Missed_occurrences_default_to_skip_with_no_tenant_schedule_and_the_default_key()
    {
        var options = new RecurringCommandHostOptions();

        options.MissedOccurrencePolicy.Should().BeOfType<RecurringMissedOccurrencePolicy.Skip>();
        options.TenantSchedule.Should().BeNull();
        options.OccurrenceKey.Should().BeNull();
    }

    [Fact]
    public async Task FireOnce_fires_one_catch_up_pass_for_a_missed_occurrence_inside_the_window()
    {
        var log = new MissedOccurrenceLog();
        var logger = new MissedOccurrenceRecordingLogger<RecurringCommandBackgroundService<MissedProbeCommand>>();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        var serviceProvider = await BuildProvider(
                log,
                timeProvider,
                lastScheduledAt: timeProvider.GetUtcNow().AddMinutes(-90),
                policy: new RecurringMissedOccurrencePolicy.FireOnce(TimeSpan.FromHours(1)),
                extra: services => services.AddSingleton<ILogger<RecurringCommandBackgroundService<MissedProbeCommand>>>(logger)).ConfigureAwait(false);
        await using var disposeServiceProvider = serviceProvider.ConfigureAwait(false);

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<MissedProbeCommand>>();
        using var cancellation = new CancellationTokenSource();

        // The catch-up pass runs at once, with no time advance: the 07:30 occurrence is thirty minutes late.
        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        logger.Entries.Should().ContainSingle(entry => entry.Id.Id == 2114);
        logger.Entries.Should().ContainSingle(entry => entry.Id.Id == 2116);
    }

    [Fact]
    public async Task FireOnce_with_two_missed_occurrences_fires_once_for_the_latest()
    {
        var log = new MissedOccurrenceLog();
        var logger = new MissedOccurrenceRecordingLogger<RecurringCommandBackgroundService<MissedProbeCommand>>();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        var serviceProvider = await BuildProvider(
                log,
                timeProvider,
                lastScheduledAt: timeProvider.GetUtcNow().AddMinutes(-150),
                policy: new RecurringMissedOccurrencePolicy.FireOnce(TimeSpan.FromHours(1)),
                extra: services => services.AddSingleton<ILogger<RecurringCommandBackgroundService<MissedProbeCommand>>>(logger)).ConfigureAwait(false);
        await using var disposeServiceProvider = serviceProvider.ConfigureAwait(false);

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<MissedProbeCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(1).ConfigureAwait(false);

        // One catch-up pass covers the gap: the earlier miss stays skipped and the next occurrence is still ahead.
        await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
        log.Count.Should().Be(1);

        var missed = logger.Entries.Where(entry => entry.Id.Id == 2114).ToList();
        missed.Should().ContainSingle();
        missed[0].Message.Should().Contain("2");

        timeProvider.Advance(TimeSpan.FromMinutes(31));
        await log.WaitForSendsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
    }

    [Fact]
    public async Task FireOnce_ignores_a_missed_occurrence_outside_the_window()
    {
        var log = new MissedOccurrenceLog();
        var logger = new MissedOccurrenceRecordingLogger<RecurringCommandBackgroundService<MissedProbeCommand>>();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        var serviceProvider = await BuildProvider(
                log,
                timeProvider,
                lastScheduledAt: timeProvider.GetUtcNow().AddMinutes(-150),
                policy: new RecurringMissedOccurrencePolicy.FireOnce(TimeSpan.FromMinutes(10)),
                extra: services => services.AddSingleton<ILogger<RecurringCommandBackgroundService<MissedProbeCommand>>>(logger)).ConfigureAwait(false);
        await using var disposeServiceProvider = serviceProvider.ConfigureAwait(false);

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<MissedProbeCommand>>();
        using var cancellation = new CancellationTokenSource();

        // The latest miss is thirty minutes old against a ten-minute window, so the loop waits for 08:30.
        var run = service.ExecuteAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
        log.Count.Should().Be(0);

        logger.Entries.Should().ContainSingle(entry => entry.Id.Id == 2114);
        logger.Entries.Should().NotContain(entry => entry.Id.Id == 2116);

        timeProvider.Advance(TimeSpan.FromMinutes(31));
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
    }

    [Fact]
    public async Task Skip_waits_for_the_next_occurrence()
    {
        var log = new MissedOccurrenceLog();
        var logger = new MissedOccurrenceRecordingLogger<RecurringCommandBackgroundService<MissedProbeCommand>>();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        var serviceProvider = await BuildProvider(
                log,
                timeProvider,
                lastScheduledAt: timeProvider.GetUtcNow().AddMinutes(-90),
                policy: new RecurringMissedOccurrencePolicy.Skip(),
                extra: services => services.AddSingleton<ILogger<RecurringCommandBackgroundService<MissedProbeCommand>>>(logger)).ConfigureAwait(false);
        await using var disposeServiceProvider = serviceProvider.ConfigureAwait(false);

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<MissedProbeCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
        log.Count.Should().Be(0);

        logger.Entries.Should().ContainSingle(entry => entry.Id.Id == 2114);

        timeProvider.Advance(TimeSpan.FromMinutes(31));
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
    }

    [Fact]
    public async Task Fresh_loop_with_no_history_waits_without_logging_a_miss()
    {
        var log = new MissedOccurrenceLog();
        var logger = new MissedOccurrenceRecordingLogger<RecurringCommandBackgroundService<MissedProbeCommand>>();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        var serviceProvider = await BuildProvider(
                log,
                timeProvider,
                lastScheduledAt: null,
                policy: new RecurringMissedOccurrencePolicy.FireOnce(TimeSpan.FromHours(1)),
                extra: services => services.AddSingleton<ILogger<RecurringCommandBackgroundService<MissedProbeCommand>>>(logger)).ConfigureAwait(false);
        await using var disposeServiceProvider = serviceProvider.ConfigureAwait(false);

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<MissedProbeCommand>>();
        using var cancellation = new CancellationTokenSource();

        // No record proves nothing was missed: an in-memory store cleared by a restart waits for 09:00.
        var run = service.ExecuteAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
        log.Count.Should().Be(0);
        logger.Entries.Should().NotContain(entry => entry.Id.Id == 2114);

        timeProvider.Advance(TimeSpan.FromMinutes(61));
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
    }

    [Fact]
    public async Task Truncated_backlog_skips_to_now_without_a_catch_up_pass()
    {
        var log = new MissedOccurrenceLog();
        var logger = new MissedOccurrenceRecordingLogger<RecurringCommandBackgroundService<MissedProbeCommand>>();
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        var serviceProvider = await BuildProvider(
                log,
                timeProvider,
                lastScheduledAt: timeProvider.GetUtcNow().AddHours(-1500),
                policy: new RecurringMissedOccurrencePolicy.FireOnce(TimeSpan.FromDays(100)),
                extra: services => services.AddSingleton<ILogger<RecurringCommandBackgroundService<MissedProbeCommand>>>(logger)).ConfigureAwait(false);
        await using var disposeServiceProvider = serviceProvider.ConfigureAwait(false);

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<MissedProbeCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);
        log.Count.Should().Be(0);

        var truncated = logger.Entries.Where(entry => entry.Id.Id == 2115).ToList();
        truncated.Should().ContainSingle();
        truncated[0].Message.Should().Contain("1000");
        logger.Entries.Should().NotContain(entry => entry.Id.Id == 2116);

        timeProvider.Advance(TimeSpan.FromMinutes(61));
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void FireOnce_rejects_a_non_positive_catch_up_window_at_composition(int minutes)
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<MissedProbeCommandHandler>()
                .AddRecurring(
                    () => new MissedProbeCommand(),
                    host => host.MissedOccurrencePolicy =
                        new RecurringMissedOccurrencePolicy.FireOnce(TimeSpan.FromMinutes(minutes))));
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Undefined_lease_scope_is_refused_at_composition()
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<MissedProbeCommandHandler>()
                .AddRecurring(
                    () => new MissedProbeCommand(),
                    host => host.LeaseScope = (RecurringLeaseScope) 42));
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Null_missed_policy_is_refused_at_composition()
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<MissedProbeCommandHandler>()
                .AddRecurring(
                    () => new MissedProbeCommand(),
                    host => host.MissedOccurrencePolicy = null!));
        });

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Null_source_identity_is_refused_at_composition()
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<MissedProbeCommandHandler>()
                .AddRecurring(
                    () => new MissedProbeCommand(),
                    host => host.SourceIdentity = null!));
        });

        act.Should().Throw<ArgumentNullException>();
    }

    private static async Task<ServiceProvider> BuildProvider(
        MissedOccurrenceLog log,
        TimeProvider timeProvider,
        DateTimeOffset? lastScheduledAt,
        RecurringMissedOccurrencePolicy policy,
        Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(log);
        extra?.Invoke(services);

        var provider = services.AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging => messaging.UseTimeProvider(timeProvider));

            registry.AddCommands(commands => commands
                .Register<MissedProbeCommandHandler>()
                .AddRecurring(
                    () => new MissedProbeCommand(),
                    host =>
                    {
                        host.Interval = TimeSpan.FromMilliseconds(5);
                        host.Schedule = RecurringSchedules.Every(TimeSpan.FromHours(1));
                        host.MissedOccurrencePolicy = policy;
                    }));
        }).BuildServiceProvider();

        if (lastScheduledAt is not null)
        {
            var loopName = typeof(MissedProbeCommand).FullName!;
            var store = provider.GetRequiredService<IRecurringOccurrenceStore>();
            var recorded = await store.TryRecordAsync(
                    new RecurringOccurrence(
                        loopName,
                        null,
                        DefaultRecurringOccurrenceKey.Instance.CreateKey(loopName, null, lastScheduledAt.Value),
                        lastScheduledAt.Value,
                        lastScheduledAt.Value),
                    CancellationToken.None)
                .ConfigureAwait(false);
            recorded.Should().BeTrue();
        }

        return provider;
    }

    /// <summary>
    ///     Counts sends and lets a test wait for a number of them without racing a fixed delay.
    /// </summary>
    public sealed class MissedOccurrenceLog : IDisposable
    {
        /// <summary>
        ///     Signals each time a send completes so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The number of sends recorded so far.
        /// </summary>
        private int _count;

        /// <summary>
        ///     Gets the number of sends recorded so far.
        /// </summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>
        ///     Records one send.
        /// </summary>
        public void Record()
        {
            Interlocked.Increment(ref _count);
            _signal.Release();
        }

        /// <summary>
        ///     Waits until at least <paramref name="target" /> sends have been recorded.
        /// </summary>
        /// <param name="target">The number of sends to wait for.</param>
        /// <returns>A task that completes once the count is reached.</returns>
        public async Task WaitForSendsAsync(int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (Count < target)
            {
                await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Releases the signal used to wait for sends.
        /// </summary>
        public void Dispose()
        {
            _signal.Dispose();
        }
    }

    /// <summary>
    ///     Captures log entries so a test can assert the missed-occurrence events the loop logged.
    /// </summary>
    /// <typeparam name="T">The logged category type.</typeparam>
    public sealed class MissedOccurrenceRecordingLogger<T> : ILogger<T>
    {
        /// <summary>
        ///     The entries logged so far, in the order they were written.
        /// </summary>
        private readonly List<MissedOccurrenceLogEntry> _entries = [];

        /// <summary>
        ///     Gets the entries logged so far, in the order they were written.
        /// </summary>
        public IReadOnlyList<MissedOccurrenceLogEntry> Entries
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
                _entries.Add(new MissedOccurrenceLogEntry(logLevel, eventId, formatter(state, exception)));
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
    public sealed record MissedOccurrenceLogEntry(LogLevel Level, EventId Id, string Message);

    /// <summary>
    ///     A command sent by the missed-occurrence test loop.
    /// </summary>
    public sealed class MissedProbeCommand : ICommand;

    private sealed class MissedProbeCommandHandler : ICommandHandler<MissedProbeCommand>
    {
        private readonly MissedOccurrenceLog _log;

        public MissedProbeCommandHandler(MissedOccurrenceLog log)
        {
            _log = log;
        }

        public Task HandleAsync(MissedProbeCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record();
            return Task.CompletedTask;
        }
    }
}
