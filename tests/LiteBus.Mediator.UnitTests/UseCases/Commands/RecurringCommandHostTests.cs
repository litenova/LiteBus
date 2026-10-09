using LiteBus.Commands.Abstractions;
using LiteBus.Commands;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Covers the recurring command loop: that it sends, that it keeps sending after a failure, that a result
///     reporting more work skips the interval, and that it composes through the command module builder.
/// </summary>
public sealed class RecurringCommandHostTests
{
    [Fact]
    public async Task Loop_sends_the_command_repeatedly()
    {
        var log = new SendLog();
        await using var serviceProvider = BuildProvider(log, host => host.Interval = TimeSpan.FromMilliseconds(5));

        var service = ResolveLoop<DrainCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(3).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [Fact]
    public async Task Loop_survives_a_failing_pass_and_keeps_sending()
    {
        var log = new SendLog { FailUntil = 2 };
        await using var serviceProvider = BuildProvider(
            log,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.ErrorBackoff = TimeSpan.FromMilliseconds(5);
            });

        var service = ResolveLoop<DrainCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(4).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);

        // The loop must not surface the handler's failure: ending here would stop the work for the life of the host.
        await run.ConfigureAwait(false);

        log.Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task A_disabled_loop_never_sends()
    {
        var log = new SendLog();
        await using var serviceProvider = BuildProvider(log, host => host.Enabled = false);

        var service = ResolveLoop<DrainCommand>(serviceProvider);

        await service.ExecuteAsync(CancellationToken.None).ConfigureAwait(false);

        log.Count.Should().Be(0);
    }

    [Fact]
    public async Task A_result_reporting_more_work_sends_again_without_waiting()
    {
        var log = new SendLog { HasMoreWorkUntil = 5 };

        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<ReportingCommandHandler>()
                .AddRecurring(
                    () => new ReportingCommand(),
                    host => host.Interval = TimeSpan.FromHours(1)));
        }).AddSingleton(log).BuildServiceProvider();

        var service = ResolveLoop<ReportingCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        var run = service.ExecuteAsync(cancellation.Token);

        // The interval is an hour, so reaching five sends is only possible when the report skips the delay.
        await log.WaitForSendsAsync(5).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Count.Should().BeGreaterThanOrEqualTo(5);
    }

    [Fact]
    public void The_loop_is_registered_as_a_background_service_on_the_host_manifest()
    {
        var services = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<DrainCommandHandler>()
                .AddRecurring(() => new DrainCommand()));
        });

        using var serviceProvider = services.AddSingleton(new SendLog()).BuildServiceProvider();
        var manifest = serviceProvider.GetRequiredService<LiteBusHostManifest>();

        manifest.BackgroundServices.Should().Contain(typeof(RecurringCommandBackgroundService<DrainCommand>));
    }

    [Fact]
    public async Task A_concurrency_conflict_skips_the_error_backoff()
    {
        var log = new SendLog { ConflictUntil = 2 };
        await using var serviceProvider = BuildProvider(
            log,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);

                // A conflict that waited out this backoff would never reach the fourth send inside the wait timeout.
                host.ErrorBackoff = TimeSpan.FromHours(1);
            });

        var service = ResolveLoop<DrainCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(4).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Count.Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void The_default_conflict_predicate_recognizes_wrapped_and_aggregated_conflicts()
    {
        var conflict = new StubConcurrencyException();

        RecurringCommandHostOptions.IsConcurrencyConflictByTypeName(conflict).Should().BeTrue();
        RecurringCommandHostOptions.IsConcurrencyConflictByTypeName(new InvalidOperationException("wrapper", conflict))
            .Should().BeTrue();
        RecurringCommandHostOptions.IsConcurrencyConflictByTypeName(
                new AggregateException(new InvalidOperationException("other"), conflict))
            .Should().BeTrue();
        RecurringCommandHostOptions.IsConcurrencyConflictByTypeName(new TimeoutException("database unreachable"))
            .Should().BeFalse();
    }

    [Fact]
    public void A_negative_interval_is_refused_at_composition()
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<DrainCommandHandler>()
                .AddRecurring(() => new DrainCommand(), host => host.Interval = TimeSpan.FromSeconds(-1)));
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static ServiceProvider BuildProvider(SendLog log, Action<RecurringCommandHostOptions> configure)
    {
        return new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<DrainCommandHandler>()
                .AddRecurring(() => new DrainCommand(), configure));
        }).AddSingleton(log).BuildServiceProvider();
    }

    private static IBackgroundService ResolveLoop<TCommand>(IServiceProvider serviceProvider)
        where TCommand : notnull
    {
        return serviceProvider.GetRequiredService<RecurringCommandBackgroundService<TCommand>>();
    }

    /// <summary>
    ///     Counts sends and lets a test wait for a number of them without racing a fixed delay.
    /// </summary>
    public sealed class SendLog : IDisposable
    {
        /// <summary>
        ///     Signals each time a send completes so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     Releases the signal used to wait for sends.
        /// </summary>
        public void Dispose()
        {
            _signal.Dispose();
        }

        /// <summary>
        ///     The number of sends recorded so far.
        /// </summary>
        private int _count;

        /// <summary>
        ///     Gets the number of sends recorded so far.
        /// </summary>
        public int Count => Volatile.Read(ref _count);

        /// <summary>
        ///     Gets or sets the send number up to which the handler throws.
        /// </summary>
        public int FailUntil { get; init; }

        /// <summary>
        ///     Gets or sets the send number up to which the handler throws a concurrency conflict.
        /// </summary>
        public int ConflictUntil { get; init; }

        /// <summary>
        ///     Gets or sets the send number up to which the result reports more work.
        /// </summary>
        public int HasMoreWorkUntil { get; init; }

        /// <summary>
        ///     Records one send and reports its ordinal.
        /// </summary>
        /// <returns>The one-based ordinal of this send.</returns>
        public int Record()
        {
            var ordinal = Interlocked.Increment(ref _count);
            _signal.Release();
            return ordinal;
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
    }

    public sealed class DrainCommand : ICommand;

    /// <summary>
    ///     Stands in for a persistence library's optimistic-concurrency exception, recognized by its type name.
    /// </summary>
    public sealed class StubConcurrencyException : Exception
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="StubConcurrencyException" /> class.
        /// </summary>
        public StubConcurrencyException()
            : base("Another instance changed the same record.")
        {
        }
    }

    public sealed record DrainResult(bool HasMoreWork) : IRecurringWorkReport;

    public sealed class ReportingCommand : ICommand<DrainResult>;

    private sealed class DrainCommandHandler : ICommandHandler<DrainCommand>
    {
        private readonly SendLog _log;

        public DrainCommandHandler(SendLog log)
        {
            _log = log;
        }

        public Task HandleAsync(DrainCommand message, CancellationToken cancellationToken = default)
        {
            var ordinal = _log.Record();

            if (ordinal <= _log.FailUntil)
            {
                throw new InvalidOperationException($"pass {ordinal} failed");
            }

            if (ordinal <= _log.ConflictUntil)
            {
                throw new StubConcurrencyException();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ReportingCommandHandler : ICommandHandler<ReportingCommand, DrainResult>
    {
        private readonly SendLog _log;

        public ReportingCommandHandler(SendLog log)
        {
            _log = log;
        }

        public Task<DrainResult> HandleAsync(ReportingCommand message, CancellationToken cancellationToken = default)
        {
            var ordinal = _log.Record();
            return Task.FromResult(new DrainResult(ordinal < _log.HasMoreWorkUntil));
        }
    }
}
