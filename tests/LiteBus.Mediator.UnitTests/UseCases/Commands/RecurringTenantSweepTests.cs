using System.Runtime.CompilerServices;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Covers the recurring per-tenant sweep: one command per tenant with the tenant identity, per-tenant failure
///     isolation, the instance, type, and registry tenant sources, and composition without a source.
/// </summary>
public sealed class RecurringTenantSweepTests
{
    [Fact]
    public async Task Sweep_sends_one_command_per_tenant_with_the_tenant_identity()
    {
        var log = new SweepLog();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands.AddRecurringForEachTenant(
                tenant => new SweepCommand { Tenant = tenant },
                new ScriptedTenantSource(["alpha", "beta"]),
                host => host.Interval = TimeSpan.FromMilliseconds(5)));

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<SweepCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Should().HaveCountGreaterThanOrEqualTo(2);
        log.Attempts.Take(2).Should().Equal(new SweepAttempt("alpha", "alpha"), new SweepAttempt("beta", "beta"));
    }

    [Fact]
    public async Task One_failing_tenant_does_not_stop_the_sweep()
    {
        var log = new SweepLog();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands.AddRecurringForEachTenant(
                tenant => new SweepCommand { Tenant = tenant },
                new ScriptedTenantSource(["ok-a", "boom", "ok-b"]),
                host =>
                {
                    host.Interval = TimeSpan.FromMilliseconds(5);
                    host.ErrorBackoff = TimeSpan.FromMilliseconds(5);
                }));

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<SweepCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);

        // The first sweep attempts every tenant in order even though the middle one throws.
        await log.WaitForAttemptsAsync(3).ConfigureAwait(false);
        log.Attempts.Take(3).Select(attempt => attempt.Tenant).Should().Equal("ok-a", "boom", "ok-b");

        // The loop survives the failing sweep and sweeps again.
        await log.WaitForAttemptsAsync(6).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Should().HaveCountGreaterThanOrEqualTo(6);
    }

    [Fact]
    public async Task A_blank_tenant_is_counted_and_skipped()
    {
        var log = new SweepLog();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands.AddRecurringForEachTenant(
                tenant => new SweepCommand { Tenant = tenant },
                new ScriptedTenantSource(["", "ok"]),
                host =>
                {
                    host.Interval = TimeSpan.FromMilliseconds(5);
                    host.ErrorBackoff = TimeSpan.FromMilliseconds(5);
                }));

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<SweepCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        // Blank identifiers never reach a handler: only the named tenant ran, across two sweeps.
        log.Attempts.Should().HaveCountGreaterThanOrEqualTo(2);
        log.Attempts.Should().OnlyContain(attempt => attempt.Tenant == "ok");
    }

    [Fact]
    public async Task Refused_tenant_commands_are_isolated_and_logged()
    {
        var log = new SweepLog();
        var logger = new SweepRecordingLogger<RecurringTenantSweepBackgroundService<SweepCommand>>();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands
                .Register<DenyingSweepGuard>()
                .Register<RejectingSweepValidator>()
                .AddRecurringForEachTenant(
                    tenant => new SweepCommand { Tenant = tenant },
                    new ScriptedTenantSource(["ok-a", "blocked", "bad", "ok-b"]),
                    host =>
                    {
                        host.Interval = TimeSpan.FromMilliseconds(5);
                        host.ErrorBackoff = TimeSpan.FromHours(1);
                    }),
            services => services.AddSingleton<ILogger<RecurringTenantSweepBackgroundService<SweepCommand>>>(logger));

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<SweepCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(2).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Select(attempt => attempt.Tenant).Should().Equal("ok-a", "ok-b");

        var refusals = logger.Entries
            .Where(entry => entry.Level == LogLevel.Warning && entry.Id.Id == 2111)
            .ToList();
        refusals.Should().HaveCount(2);
        refusals[0].Message.Should().Contain("blocked").And.Contain("SWEEP_DENIED");
        refusals[1].Message.Should().Contain("bad").And.Contain("SWEEP_INVALID");
    }

    [Fact]
    public async Task Type_source_is_resolved_from_a_dispatch_scope_on_each_pass()
    {
        var log = new SweepLog();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands.AddRecurringForEachTenant<SweepCommand, FixedTenantSource>(
                tenant => new SweepCommand { Tenant = tenant },
                host => host.Interval = TimeSpan.FromMilliseconds(5)));

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<SweepCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Should().OnlyContain(attempt => attempt == new SweepAttempt("type-a", "type-a"));
    }

    [Fact]
    public async Task Registry_source_is_used_when_no_source_is_passed()
    {
        var log = new SweepLog();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands
                .AddRecurringForEachTenant(
                    tenant => new SweepCommand { Tenant = tenant },
                    host => host.Interval = TimeSpan.FromMilliseconds(5))
                .RegisterChildModule(new TenantSourceModule(new ScriptedTenantSource(["di-a"]))));

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<SweepCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForAttemptsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Attempts.Should().OnlyContain(attempt => attempt == new SweepAttempt("di-a", "di-a"));
    }

    [Fact]
    public void Sweep_without_tenant_source_fails_composition()
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<SweepCommandHandler>()
                .AddRecurringForEachTenant(tenant => new SweepCommand { Tenant = tenant }));
        });

        act.Should().Throw<LiteBusConfigurationException>().WithMessage("*IRecurringTenantSource*");
    }

    private static ServiceProvider BuildProvider(
        SweepLog log,
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
                commands.Register<SweepCommandHandler>();
                register(commands);
            });
        }).BuildServiceProvider();
    }

    /// <summary>
    ///     Records the sweep attempts handlers observe and lets a test wait for a number of them.
    /// </summary>
    public sealed class SweepLog : IDisposable
    {
        /// <summary>
        ///     Signals each time an attempt completes so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The attempts observed so far, in the order they were recorded.
        /// </summary>
        private readonly List<SweepAttempt> _attempts = [];

        /// <summary>
        ///     Gets the attempts observed so far, in the order they were recorded.
        /// </summary>
        public IReadOnlyList<SweepAttempt> Attempts
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
        public void Record(SweepAttempt attempt)
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
    public sealed record SweepAttempt(string Tenant, string? ObservedTenant);

    /// <summary>
    ///     Captures log entries so a test can assert the refusal the sweep logged.
    /// </summary>
    /// <typeparam name="T">The logged category type.</typeparam>
    public sealed class SweepRecordingLogger<T> : ILogger<T>
    {
        /// <summary>
        ///     The entries logged so far, in the order they were written.
        /// </summary>
        private readonly List<SweepLogEntry> _entries = [];

        /// <summary>
        ///     Gets the entries logged so far, in the order they were written.
        /// </summary>
        public IReadOnlyList<SweepLogEntry> Entries
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
                _entries.Add(new SweepLogEntry(logLevel, eventId, formatter(state, exception)));
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
    public sealed record SweepLogEntry(LogLevel Level, EventId Id, string Message);

    /// <summary>
    ///     A command built once per tenant by the sweep.
    /// </summary>
    public sealed class SweepCommand : ICommand
    {
        public string Tenant { get; init; } = "";
    }

    private sealed class SweepCommandHandler : ICommandHandler<SweepCommand>
    {
        private readonly SweepLog _log;

        public SweepCommandHandler(SweepLog log)
        {
            _log = log;
        }

        public Task HandleAsync(SweepCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(new SweepAttempt(message.Tenant, AmbientExecutionContext.Current.Identity.TenantId));

            if (message.Tenant == "boom")
            {
                throw new InvalidOperationException("tenant 'boom' failed");
            }

            return Task.CompletedTask;
        }
    }

    private sealed class DenyingSweepGuard : ICommandGuard<SweepCommand>
    {
        public Task<Verdict> DecideAsync(SweepCommand message, CancellationToken cancellationToken = default)
        {
            return message.Tenant == "blocked"
                ? Task.FromResult(Verdict.Deny("the sweep test denies this tenant", "SWEEP_DENIED"))
                : Task.FromResult(Verdict.Allow);
        }
    }

    private sealed class RejectingSweepValidator : ICommandValidator<SweepCommand>
    {
        public Task<Validity> ValidateAsync(SweepCommand message, CancellationToken cancellationToken = default)
        {
            return message.Tenant == "bad"
                ? Task.FromResult(Validity.Invalid("the sweep test rejects this tenant", "Tenant", "SWEEP_INVALID"))
                : Task.FromResult(Validity.Valid);
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

    private sealed class FixedTenantSource : IRecurringTenantSource
    {
        public async IAsyncEnumerable<string> GetTenantsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask.ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            yield return "type-a";
        }
    }

    private sealed class TenantSourceModule : IModule
    {
        private readonly IRecurringTenantSource _source;

        public TenantSourceModule(IRecurringTenantSource source)
        {
            _source = source;
        }

        public void Build(IModuleConfiguration configuration)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IRecurringTenantSource), _source));
        }
    }
}
