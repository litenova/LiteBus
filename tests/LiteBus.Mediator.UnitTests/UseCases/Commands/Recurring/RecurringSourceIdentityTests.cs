using System.Runtime.CompilerServices;
using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Tenancy;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands.Recurring;

/// <summary>
///     Covers the identity recurring source reads run under: the system default, a configured identity, isolation from
///     the ambient context through a root mediation scope, and inheritance by queries a source sends.
/// </summary>
public sealed class RecurringSourceIdentityTests
{
    [Fact]
    public void Source_identity_defaults_to_system()
    {
        new RecurringCommandHostOptions().SourceIdentity.Should().Be(ExecutionIdentity.System);
    }

    [Fact]
    public async Task Tenant_source_runs_under_the_system_identity_inside_a_root_scope()
    {
        var log = new SourceIdentityLog();
        var source = new IdentityRecordingTenantSource(["alpha"], log);
        await using var serviceProvider = BuildSweepProvider(
            log,
            source,
            host => host.Interval = TimeSpan.FromMilliseconds(5));

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<SourceIdentityCommand>>();
        using var cancellation = new CancellationTokenSource();

        // The outer tenant must not leak into the source read: the loop establishes a root mediation scope.
        using (AmbientExecutionContext.CreateScope(new TestExecutionContext(ExecutionIdentity.ForTenant("outer"))))
        {
            var run = service.ExecuteAsync(cancellation.Token);
            await log.WaitForObservationsAsync(1).ConfigureAwait(false);
            await cancellation.CancelAsync().ConfigureAwait(false);
            await run.ConfigureAwait(false);
        }

        log.Identities.Should().OnlyContain(identity => identity == ExecutionIdentity.System);
    }

    [Fact]
    public async Task Tenant_source_runs_under_a_configured_source_identity()
    {
        var log = new SourceIdentityLog();
        var source = new IdentityRecordingTenantSource(["alpha"], log);
        await using var serviceProvider = BuildSweepProvider(
            log,
            source,
            host =>
            {
                host.Interval = TimeSpan.FromMilliseconds(5);
                host.SourceIdentity = ExecutionIdentity.ForTenant("source-tenant");
            });

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<SourceIdentityCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForObservationsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Identities.Should().OnlyContain(identity => identity.TenantId == "source-tenant");
    }

    [Fact]
    public async Task Batch_source_runs_under_the_system_identity()
    {
        var log = new SourceIdentityLog();
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<SourceIdentityBatchCommandHandler>()
                .AddRecurringBatch<IdentityRecordingBatchSource>(
                    host => host.Interval = TimeSpan.FromMilliseconds(5)));
        }).AddSingleton(log).BuildServiceProvider();

        var service = serviceProvider.GetRequiredService<RecurringCommandBatchBackgroundService<IdentityRecordingBatchSource>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForObservationsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Identities.Should().OnlyContain(identity => identity == ExecutionIdentity.System);
    }

    [Fact]
    public async Task Query_sent_by_a_tenant_source_inherits_the_source_identity()
    {
        var log = new SourceIdentityLog();
        var attempts = new SourceIdentityAttemptLog();
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<SourceIdentityCommandHandler>()
                .AddRecurringForEachTenant<SourceIdentityCommand, QueryingTenantSource>(
                    tenant => new SourceIdentityCommand { Tenant = tenant },
                    host => host.Interval = TimeSpan.FromMilliseconds(5)));

            registry.AddQueries(queries => queries.Register<ListWorkspaceIdsQueryHandler>());
        }).AddSingleton(log).AddSingleton(attempts).BuildServiceProvider();

        var service = serviceProvider.GetRequiredService<RecurringTenantSweepBackgroundService<SourceIdentityCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await attempts.WaitForAttemptsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        attempts.Tenants.Should().OnlyContain(tenant => tenant == "queried-a");
        log.Identities.Should().NotBeEmpty();
        log.Identities.Should().OnlyContain(identity => identity == ExecutionIdentity.System);
    }

    private static ServiceProvider BuildSweepProvider(
        SourceIdentityLog log,
        IRecurringTenantSource source,
        Action<RecurringCommandHostOptions> configure)
    {
        return new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<SourceIdentityCommandHandler>()
                .AddRecurringForEachTenant(
                    tenant => new SourceIdentityCommand { Tenant = tenant },
                    source,
                    configure));
        }).AddSingleton(log).AddSingleton(new SourceIdentityAttemptLog()).BuildServiceProvider();
    }

    /// <summary>
    ///     Records the identities source reads and query handlers observe, and lets a test wait for them.
    /// </summary>
    public sealed class SourceIdentityLog : IDisposable
    {
        /// <summary>
        ///     Signals each time an identity is recorded so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The identities observed so far, in the order they were recorded.
        /// </summary>
        private readonly List<ExecutionIdentity> _identities = [];

        /// <summary>
        ///     Gets the identities observed so far, in the order they were recorded.
        /// </summary>
        public IReadOnlyList<ExecutionIdentity> Identities
        {
            get
            {
                lock (_identities)
                {
                    return [.. _identities];
                }
            }
        }

        /// <summary>
        ///     Records one observed identity.
        /// </summary>
        /// <param name="identity">The observed identity.</param>
        public void Record(ExecutionIdentity identity)
        {
            lock (_identities)
            {
                _identities.Add(identity);
            }

            _signal.Release();
        }

        /// <summary>
        ///     Waits until at least <paramref name="target" /> identities have been recorded.
        /// </summary>
        /// <param name="target">The number of identities to wait for.</param>
        /// <returns>A task that completes once the count is reached.</returns>
        public async Task WaitForObservationsAsync(int target)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            while (Identities.Count < target)
            {
                await _signal.WaitAsync(timeout.Token).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Releases the signal used to wait for observations.
        /// </summary>
        public void Dispose()
        {
            _signal.Dispose();
        }
    }

    /// <summary>
    ///     Records the tenants swept commands ran for and lets a test wait for a number of them.
    /// </summary>
    public sealed class SourceIdentityAttemptLog : IDisposable
    {
        /// <summary>
        ///     Signals each time an attempt completes so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The tenants observed so far, in the order they were recorded.
        /// </summary>
        private readonly List<string> _tenants = [];

        /// <summary>
        ///     Gets the tenants observed so far, in the order they were recorded.
        /// </summary>
        public IReadOnlyList<string> Tenants
        {
            get
            {
                lock (_tenants)
                {
                    return [.. _tenants];
                }
            }
        }

        /// <summary>
        ///     Records one attempt.
        /// </summary>
        /// <param name="tenant">The tenant the command ran for.</param>
        public void Record(string tenant)
        {
            lock (_tenants)
            {
                _tenants.Add(tenant);
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

            while (Tenants.Count < target)
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
    ///     A command built once per tenant by the sweep.
    /// </summary>
    public sealed class SourceIdentityCommand : ICommand
    {
        public string Tenant { get; init; } = "";
    }

    /// <summary>
    ///     A command a batch source test produces.
    /// </summary>
    public sealed class SourceIdentityBatchCommand : ICommand;

    /// <summary>
    ///     A query a tenant source test sends to list tenants.
    /// </summary>
    public sealed class ListWorkspaceIdsQuery : IQuery<IReadOnlyList<string>>;

    private sealed class SourceIdentityCommandHandler : ICommandHandler<SourceIdentityCommand>
    {
        private readonly SourceIdentityAttemptLog _attempts;

        public SourceIdentityCommandHandler(SourceIdentityAttemptLog attempts)
        {
            _attempts = attempts;
        }

        public Task HandleAsync(SourceIdentityCommand message, CancellationToken cancellationToken = default)
        {
            _attempts.Record(message.Tenant);
            return Task.CompletedTask;
        }
    }

    private sealed class SourceIdentityBatchCommandHandler : ICommandHandler<SourceIdentityBatchCommand>
    {
        public Task HandleAsync(SourceIdentityBatchCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class ListWorkspaceIdsQueryHandler : IQueryHandler<ListWorkspaceIdsQuery, IReadOnlyList<string>>
    {
        private readonly SourceIdentityLog _log;

        public ListWorkspaceIdsQueryHandler(SourceIdentityLog log)
        {
            _log = log;
        }

        public Task<IReadOnlyList<string>> HandleAsync(ListWorkspaceIdsQuery message, CancellationToken cancellationToken = default)
        {
            _log.Record(AmbientExecutionContext.Current.Identity);
            return Task.FromResult<IReadOnlyList<string>>(["queried-a"]);
        }
    }

    private sealed class IdentityRecordingTenantSource : IRecurringTenantSource
    {
        private readonly string[] _tenants;
        private readonly SourceIdentityLog _log;

        public IdentityRecordingTenantSource(string[] tenants, SourceIdentityLog log)
        {
            _tenants = tenants;
            _log = log;
        }

        public async IAsyncEnumerable<string> GetTenantsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _log.Record(AmbientExecutionContext.Current.Identity);
            await Task.CompletedTask.ConfigureAwait(false);

            foreach (var tenant in _tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return tenant;
            }
        }
    }

    private sealed class IdentityRecordingBatchSource : IRecurringCommandBatchSource
    {
        private readonly SourceIdentityLog _log;

        public IdentityRecordingBatchSource(SourceIdentityLog log)
        {
            _log = log;
        }

        public Task<RecurringCommandBatch> CreateBatchAsync(CancellationToken cancellationToken)
        {
            _log.Record(AmbientExecutionContext.Current.Identity);
            return Task.FromResult(RecurringCommandBatch.Empty);
        }
    }

    private sealed class QueryingTenantSource : IRecurringTenantSource
    {
        private readonly IQueryMediator _queries;

        public QueryingTenantSource(IQueryMediator queries)
        {
            _queries = queries;
        }

        public async IAsyncEnumerable<string> GetTenantsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            var tenants = await _queries
                .QueryAsync(new ListWorkspaceIdsQuery(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            foreach (var tenant in tenants)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return tenant;
            }
        }
    }
}
