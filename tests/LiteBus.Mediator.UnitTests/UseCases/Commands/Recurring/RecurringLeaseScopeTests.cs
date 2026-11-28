using System.Runtime.CompilerServices;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.Recurring;

/// <summary>
///     Covers the recurring lease scope: the loop default, one lease per tenant, one lease per scheduled occurrence
///     of one tenant, and a contended tenant skipped while the sweep continues.
/// </summary>
public sealed class RecurringLeaseScopeTests
{
    [Fact]
    public void Loop_is_the_default_scope()
    {
        new RecurringCommandHostOptions().LeaseScope.Should().Be(RecurringLeaseScope.Loop);
    }

    [Fact]
    public async Task Loop_scope_acquires_one_lease_per_pass_for_a_sweep()
    {
        var log = new LeaseScopeLog();
        var lease = new FakeLease();
        var loopName = typeof(LeaseScopeCommand).FullName!;
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            ["alpha", "beta"],
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
            });

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<LeaseScopeCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        lease.Acquisitions.Should().BeGreaterThanOrEqualTo(1);
        lease.Names.Should().OnlyContain(name => name == loopName);
    }

    [Fact]
    public async Task Tenant_scope_acquires_one_lease_per_tenant()
    {
        var log = new LeaseScopeLog();
        var lease = new FakeLease();
        var loopName = typeof(LeaseScopeCommand).FullName!;
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            ["alpha", "beta"],
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
                host.LeaseScope = RecurringLeaseScope.Tenant;
            });

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<LeaseScopeCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        lease.Names.Should().Contain($"{loopName}/alpha");
        lease.Names.Should().Contain($"{loopName}/beta");
        lease.Names.Should().OnlyContain(name => name == $"{loopName}/alpha" || name == $"{loopName}/beta");
        await lease.WaitForReleasesAsync(lease.Acquisitions).ConfigureAwait(false);
        lease.Releases.Should().Be(lease.Acquisitions);
    }

    [Fact]
    public async Task Tenant_scope_skips_a_contended_tenant_and_continues()
    {
        var log = new LeaseScopeLog();
        var logger = new LeaseScopeRecordingLogger<RecurringTenantSweepBackgroundService<LeaseScopeCommand>>();
        var lease = new FakeLease { GrantWhen = name => !name.EndsWith("/blocked", StringComparison.Ordinal) };
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            ["ok-a", "blocked", "ok-b"],
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
                host.LeaseScope = RecurringLeaseScope.Tenant;
            },
            services => services.AddSingleton<ILogger<RecurringTenantSweepBackgroundService<LeaseScopeCommand>>>(logger));

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<LeaseScopeCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(4).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Should().OnlyContain(attempt => attempt.Tenant == "ok-a" || attempt.Tenant == "ok-b");
        logger.Entries.Should().Contain(entry => entry.Id.Id == 2119 && entry.Message.Contains("blocked"));
    }

    [Fact]
    public async Task Occurrence_scope_keys_leases_by_scheduled_occurrence()
    {
        var log = new LeaseScopeLog();
        var lease = new FakeLease();
        var loopName = typeof(LeaseScopeCommand).FullName!;
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero));
        await using var serviceProvider = BuildScheduledProvider(
            log,
            lease,
            timeProvider,
            ["alpha"],
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
                host.LeaseScope = RecurringLeaseScope.TenantOccurrence;
                host.TenantSchedule = RecurringSchedules
                    .DailyAt(new TimeOnly(9, 0), (_, _) => new ValueTask<TimeZoneInfo?>(TimeZoneInfo.Utc))
                    .AsRecurringSchedule();
            });

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<LeaseScopeCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        timeProvider.Advance(TimeSpan.FromMinutes(59));
        await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
        timeProvider.Advance(TimeSpan.FromMinutes(2));
        await log.WaitForAttemptsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        lease.Names.Should().HaveCount(1);
        lease.Names[0].Should().Be($"{loopName}/{loopName}/alpha/20260105T090000Z");
    }

    [Fact]
    public async Task Occurrence_scope_without_tenant_schedule_behaves_as_tenant_scope()
    {
        var log = new LeaseScopeLog();
        var lease = new FakeLease();
        var loopName = typeof(LeaseScopeCommand).FullName!;
        await using var serviceProvider = BuildProvider(
            log,
            lease,
            ["alpha", "beta"],
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.RequireLease = true;
                host.LeaseScope = RecurringLeaseScope.TenantOccurrence;
            });

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<LeaseScopeCommand>>();
        using var cancellation = new CancellationTokenSource();

        // A poll sweep has no scheduled occurrence to key by, so leases carry the tenant only.
        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        lease.Names.Should().OnlyContain(name => name == $"{loopName}/alpha" || name == $"{loopName}/beta");
    }

    private static ServiceProvider BuildProvider(
        LeaseScopeLog log,
        FakeLease lease,
        string[] tenants,
        Action<RecurringCommandHostOptions> configure,
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

            registry.AddCommands(commands => commands
                .Register<LeaseScopeCommandHandler>()
                .AddRecurringForEachTenant(
                    tenant => new LeaseScopeCommand { Tenant = tenant },
                    new ScriptedTenantSource(tenants),
                    configure)
                .RegisterChildModule(new FakeLeaseModule(lease)));
        }).BuildServiceProvider();
    }

    private static ServiceProvider BuildScheduledProvider(
        LeaseScopeLog log,
        FakeLease lease,
        TimeProvider timeProvider,
        string[] tenants,
        Action<RecurringCommandHostOptions> configure)
    {
        return new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(messaging => messaging.UseTimeProvider(timeProvider));

            registry.AddCommands(commands => commands
                .Register<LeaseScopeCommandHandler>()
                .AddRecurringForEachTenant(
                    tenant => new LeaseScopeCommand { Tenant = tenant },
                    new ScriptedTenantSource(tenants),
                    configure)
                .RegisterChildModule(new FakeLeaseModule(lease)));
        }).AddSingleton(log).BuildServiceProvider();
    }

    /// <summary>
    ///     Records the sweep attempts handlers observe and lets a test wait for a number of them.
    /// </summary>
    public sealed class LeaseScopeLog : IDisposable
    {
        /// <summary>
        ///     Signals each time an attempt completes so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The attempts observed so far, in the order they were recorded.
        /// </summary>
        private readonly List<LeaseScopeAttempt> _attempts = [];

        /// <summary>
        ///     Gets the attempts observed so far, in the order they were recorded.
        /// </summary>
        public IReadOnlyList<LeaseScopeAttempt> Attempts
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
        public void Record(LeaseScopeAttempt attempt)
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
    ///     One sweep attempt: the tenant the command was built for and the tenant the handler observed.
    /// </summary>
    public sealed record LeaseScopeAttempt(string Tenant, string? ObservedTenant);

    /// <summary>
    ///     Captures log entries so a test can assert the tenant-lease-skipped event the sweep logged.
    /// </summary>
    /// <typeparam name="T">The logged category type.</typeparam>
    public sealed class LeaseScopeRecordingLogger<T> : ILogger<T>
    {
        /// <summary>
        ///     The entries logged so far, in the order they were written.
        /// </summary>
        private readonly List<LeaseScopeLogEntry> _entries = [];

        /// <summary>
        ///     Gets the entries logged so far, in the order they were written.
        /// </summary>
        public IReadOnlyList<LeaseScopeLogEntry> Entries
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
                _entries.Add(new LeaseScopeLogEntry(logLevel, eventId, formatter(state, exception)));
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
    public sealed record LeaseScopeLogEntry(LogLevel Level, EventId Id, string Message);

    /// <summary>
    ///     A command built once per tenant by the sweep.
    /// </summary>
    public sealed class LeaseScopeCommand : ICommand
    {
        public string Tenant { get; init; } = "";
    }

    private sealed class LeaseScopeCommandHandler : ICommandHandler<LeaseScopeCommand>
    {
        private readonly LeaseScopeLog _log;

        public LeaseScopeCommandHandler(LeaseScopeLog log)
        {
            _log = log;
        }

        public Task HandleAsync(LeaseScopeCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(new LeaseScopeAttempt(message.Tenant, AmbientExecutionContext.Current.Identity.TenantId));
            return Task.CompletedTask;
        }
    }

    private sealed class ScriptedTenantSource : IRecurringTenantSource
    {
        private readonly string[] _tenants;

        public ScriptedTenantSource(string[] tenants)
        {
            _tenants = tenants;
        }

        public async IAsyncEnumerable<string> GetTenantsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            foreach (var tenant in _tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return tenant;
            }
        }
    }

    /// <summary>
    ///     Grants or refuses lease acquisitions per name while recording every attempt and release.
    /// </summary>
    private sealed class FakeLease : IRecurringCommandLease, IDisposable
    {
        private readonly SemaphoreSlim _releaseSignal = new(0);
        private readonly List<string> _names = [];
        private int _acquisitions;
        private int _releases;

        public void Dispose()
        {
            _releaseSignal.Dispose();
        }

        public Func<string, bool>? GrantWhen { get; init; }

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

                if (GrantWhen?.Invoke(leaseName) ?? true)
                {
                    handle = new FakeHandle(this);
                }
            }

            return Task.FromResult(handle);
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

            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref _owner._releases);
                _owner._releaseSignal.Release();
                _loss.Dispose();
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
}
