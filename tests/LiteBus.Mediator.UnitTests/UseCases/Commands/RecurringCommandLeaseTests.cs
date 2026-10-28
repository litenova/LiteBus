using LiteBus.Commands.Abstractions;
using LiteBus.Commands;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Covers recurring command single-flight: skipped passes, lease release on success and failure, busy-period coverage, lease loss ending the busy period, shutdown with a held lease, composition without a lease, and the default lease name.
/// </summary>
public sealed class RecurringCommandLeaseTests
{
    [Fact]
    public async Task Pass_is_skipped_when_the_lease_is_not_acquired()
    {
        var log = new SendLog();
        var lease = new FakeLease { Grant = false };
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
            });

        var service = ResolveLoop<LeaseProbeCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await lease.WaitForAttemptsAsync(3).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Count.Should().Be(0);
        lease.Releases.Should().Be(0);
    }

    [Fact]
    public async Task Pass_runs_and_releases_the_lease_when_acquired()
    {
        var log = new SendLog();
        var lease = new FakeLease();
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
            });

        var service = ResolveLoop<LeaseProbeCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(3).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Count.Should().BeGreaterThanOrEqualTo(3);
        lease.Acquisitions.Should().BeGreaterThanOrEqualTo(1);
        lease.Releases.Should().Be(lease.Acquisitions);
    }

    [Fact]
    public async Task Lease_is_released_even_when_the_send_throws()
    {
        var log = new SendLog { FailUntil = 100 };
        var lease = new FakeLease();
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.ErrorBackoff = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
            });

        var service = ResolveLoop<LeaseProbeCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await lease.WaitForAttemptsAsync(3).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Count.Should().BeGreaterThanOrEqualTo(3);
        lease.Releases.Should().Be(lease.Acquisitions);
    }

    [Fact]
    public async Task Lease_release_failure_does_not_end_the_loop()
    {
        var log = new SendLog();
        var lease = new FakeLease { ThrowOnRelease = true };
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.ErrorBackoff = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
            });

        var service = ResolveLoop<LeaseProbeCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);

        // A second acquisition is only possible when the loop survived the first release failure.
        await lease.WaitForAttemptsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task Lease_loss_stops_the_busy_period_releases_and_backs_off()
    {
        var log = new SendLog { HasMoreWorkUntil = int.MaxValue };
        var lease = new FakeLease();
        await using var serviceProvider = BuildReportingProvider(
            log,
            lease,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.ErrorBackoff = TimeSpan.FromMilliseconds(400);
                host.RequireLease = true;
            });

        var service = ResolveLoop<LeaseReportingCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(2).ConfigureAwait(false);

        lease.CancelLoss();

        // The loop must release the lost lease promptly instead of running the busy period on.
        await lease.WaitForReleasesAsync(1).ConfigureAwait(false);

        var sendsAtLoss = log.Count;
        await Task.Delay(TimeSpan.FromMilliseconds(150)).ConfigureAwait(false);

        // Still backing off: no further passes and no re-acquire while the backoff runs.
        log.Count.Should().Be(sendsAtLoss);
        lease.Acquisitions.Should().Be(1);

        // The loop survived: it re-acquires after the backoff and sends again.
        await log.WaitForSendsAsync(sendsAtLoss + 2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        lease.Acquisitions.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Lease_loss_cancels_the_in_flight_send()
    {
        var log = new SendLog { BlockUntilCancelled = true };
        var lease = new FakeLease();
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.ErrorBackoff = TimeSpan.FromMilliseconds(50);
                host.RequireLease = true;
            });

        var service = ResolveLoop<LeaseProbeCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(1).ConfigureAwait(false);

        lease.CancelLoss();

        // The blocked send must end through cancellation so the pass releases the lost lease.
        await lease.WaitForReleasesAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
    }

    [Fact]
    public async Task Shutdown_with_a_held_lease_and_failing_release_completes_cleanly()
    {
        var log = new SendLog { BlockUntilCancelled = true };
        var lease = new FakeLease { ThrowOnRelease = true };
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.ErrorBackoff = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
            });

        var service = ResolveLoop<LeaseProbeCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);

        // The loop returns with the lease held, so the shutdown-path release runs; its failure is logged, not thrown.
        await run.ConfigureAwait(false);

        lease.Releases.Should().Be(1);
    }

    [Fact]
    public async Task One_lease_covers_the_busy_period()
    {
        var log = new SendLog { HasMoreWorkUntil = 3 };
        var lease = new FakeLease();
        await using var serviceProvider = BuildReportingProvider(
            log,
            lease,
            host =>
            {
                // The interval is an hour, so a second acquisition would prove per-send leasing.
                host.Interval = TimeSpan.FromHours(1);
                host.RequireLease = true;
            });

        var service = ResolveLoop<LeaseReportingCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(3).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        lease.Acquisitions.Should().Be(1);
        lease.Releases.Should().Be(1);
    }

    [Fact]
    public void Composition_fails_when_require_lease_has_no_lease()
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<LeaseProbeCommandHandler>()
                .AddRecurring(() => new LeaseProbeCommand(), host => host.RequireLease = true));
        });

        act.Should().Throw<LiteBusConfigurationException>().WithMessage("*IRecurringCommandLease*");
    }

    [Fact]
    public async Task Lease_name_defaults_to_the_command_type_full_name()
    {
        var log = new SendLog();
        var lease = new FakeLease();
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
            });

        var service = ResolveLoop<LeaseProbeCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        lease.Names.Should().NotBeEmpty();
        lease.Names.Should().OnlyContain(name => name == typeof(LeaseProbeCommand).FullName);
    }

    [Fact]
    public async Task Custom_lease_name_is_used_when_configured()
    {
        var log = new SendLog();
        var lease = new FakeLease();
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
                host.LeaseName = "shared-loop";
            });

        var service = ResolveLoop<LeaseProbeCommand>(serviceProvider);
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        lease.Names.Should().NotBeEmpty();
        lease.Names.Should().OnlyContain(name => name == "shared-loop");
    }

    private static ServiceProvider BuildProvider(SendLog log, FakeLease lease, Action<RecurringCommandHostOptions> configure)
    {
        return new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<LeaseProbeCommandHandler>()
                .AddRecurring(() => new LeaseProbeCommand(), configure)
                .RegisterChildModule(new FakeLeaseModule(lease)));
        }).AddSingleton(log).BuildServiceProvider();
    }

    private static ServiceProvider BuildReportingProvider(SendLog log, FakeLease lease, Action<RecurringCommandHostOptions> configure)
    {
        return new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<LeaseReportingCommandHandler>()
                .AddRecurring(() => new LeaseReportingCommand(), configure)
                .RegisterChildModule(new FakeLeaseModule(lease)));
        }).AddSingleton(log).BuildServiceProvider();
    }

    private static RecurringCommandBackgroundService<TCommand> ResolveLoop<TCommand>(IServiceProvider serviceProvider)
        where TCommand : notnull
    {
        return serviceProvider.GetRequiredService<RecurringCommandBackgroundService<TCommand>>();
    }

    /// <summary>
    ///     Counts sends and lets a test wait for a number of them without racing a fixed delay.
    /// </summary>
    private sealed class SendLog : IDisposable
    {
        private readonly SemaphoreSlim _signal = new(0);
        private int _count;

        public void Dispose()
        {
            _signal.Dispose();
        }

        public int Count => Volatile.Read(ref _count);

        public int FailUntil { get; init; }

        public int HasMoreWorkUntil { get; init; }

        public bool BlockUntilCancelled { get; init; }

        public int Record()
        {
            var ordinal = Interlocked.Increment(ref _count);
            _signal.Release();
            return ordinal;
        }

        public async Task WaitForSendsAsync(int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (Count < target)
            {
                await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Grants or refuses lease acquisitions on demand while recording every attempt and release.
    /// </summary>
    private sealed class FakeLease : IRecurringCommandLease, IDisposable
    {
        private readonly SemaphoreSlim _attemptSignal = new(0);
        private readonly SemaphoreSlim _releaseSignal = new(0);

        public void Dispose()
        {
            _attemptSignal.Dispose();
            _releaseSignal.Dispose();
        }

        private readonly List<string> _names = [];
        private FakeHandle? _currentHandle;
        private int _acquisitions;
        private int _releases;

        public bool Grant { get; init; } = true;

        public bool ThrowOnRelease { get; init; }

        public int Acquisitions => Volatile.Read(ref _acquisitions);

        public int Releases => Volatile.Read(ref _releases);

        public IReadOnlyList<string> Names
        {
            get
            {
                lock (_names)
                {
                    return [.. _names];
                }
            }
        }

        public Task<IRecurringCommandLeaseHandle?> TryAcquireAsync(string leaseName, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _acquisitions);

            IRecurringCommandLeaseHandle? handle = null;

            lock (_names)
            {
                _names.Add(leaseName);

                if (Grant)
                {
                    var granted = new FakeHandle(this);
                    _currentHandle = granted;
                    handle = granted;
                }
            }

            _attemptSignal.Release();

            return Task.FromResult(handle);
        }

        public void CancelLoss()
        {
            FakeHandle? current;

            lock (_names)
            {
                current = _currentHandle;
            }

            current?.CancelLoss();
        }

        public async Task WaitForAttemptsAsync(int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (Acquisitions < target)
            {
                await _attemptSignal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        public async Task WaitForReleasesAsync(int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (Releases < target)
            {
                await _releaseSignal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        private sealed class FakeHandle : IRecurringCommandLeaseHandle
        {
            private readonly FakeLease _owner;
            private readonly CancellationTokenSource _loss = new();

            public FakeHandle(FakeLease owner)
            {
                _owner = owner;
            }

            public CancellationToken LeaseLost => _loss.Token;

            public void CancelLoss()
            {
                _loss.Cancel();
            }

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref _owner._releases);
                _owner._releaseSignal.Release();
                _loss.Dispose();

                if (_owner.ThrowOnRelease)
                {
                    throw new InvalidOperationException("The test lease release failed.");
                }

                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class FakeLeaseModule : IModule
    {
        private readonly IRecurringCommandLease _lease;

        public FakeLeaseModule(IRecurringCommandLease lease)
        {
            _lease = lease;
        }

        public void Build(IModuleConfiguration configuration)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IRecurringCommandLease), _lease));
        }
    }

    /// <summary>
    ///     A command sent by the leased test loop.
    /// </summary>
    public sealed class LeaseProbeCommand : ICommand;

    /// <summary>
    ///     The result the reporting test command produces.
    /// </summary>
    public sealed record LeaseDrainResult(bool HasMoreWork) : IRecurringWorkReport;

    /// <summary>
    ///     A command whose result reports more work, for the busy-period test.
    /// </summary>
    public sealed class LeaseReportingCommand : ICommand<LeaseDrainResult>;

    private sealed class LeaseProbeCommandHandler : ICommandHandler<LeaseProbeCommand>
    {
        private readonly SendLog _log;

        public LeaseProbeCommandHandler(SendLog log)
        {
            _log = log;
        }

        public async Task HandleAsync(LeaseProbeCommand message, CancellationToken cancellationToken = default)
        {
            var ordinal = _log.Record();

            if (ordinal <= _log.FailUntil)
            {
                throw new InvalidOperationException($"pass {ordinal} failed");
            }

            if (_log.BlockUntilCancelled)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class LeaseReportingCommandHandler : ICommandHandler<LeaseReportingCommand, LeaseDrainResult>
    {
        private readonly SendLog _log;

        public LeaseReportingCommandHandler(SendLog log)
        {
            _log = log;
        }

        public Task<LeaseDrainResult> HandleAsync(LeaseReportingCommand message, CancellationToken cancellationToken = default)
        {
            var ordinal = _log.Record();
            return Task.FromResult(new LeaseDrainResult(ordinal < _log.HasMoreWorkUntil));
        }
    }
}
