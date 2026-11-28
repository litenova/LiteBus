using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Covers that a recurring loop sends with the identity its registration supplies, and anonymously without one.
/// </summary>
public sealed class RecurringCommandIdentityTests
{
    [Fact]
    public async Task Loop_sends_with_the_identity_the_registration_supplies()
    {
        var log = new IdentityLog();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands.AddRecurring(
                () => new TenantCommand { TenantCode = "acme" },
                host => host.Interval = TimeSpan.FromMilliseconds(5),
                identity: command => ExecutionIdentity.ForTenant(command.TenantCode)));

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<TenantCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Identities.Should().OnlyContain(identity => identity.TenantId == "acme");
    }

    [Fact]
    public async Task Loop_without_identity_sends_anonymously()
    {
        var log = new IdentityLog();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands.AddRecurring(
                () => new TenantCommand { TenantCode = "acme" },
                host => host.Interval = TimeSpan.FromMilliseconds(5)));

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<TenantCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Identities.Should().OnlyContain(identity => identity == ExecutionIdentity.Anonymous);
    }

    [Fact]
    public async Task Result_bearing_command_sends_with_the_identity_the_registration_supplies()
    {
        var log = new IdentityLog();
        await using var serviceProvider = BuildProvider(
            log,
            commands => commands.AddRecurring(
                () => new TenantReportingCommand { TenantCode = "acme" },
                host => host.Interval = TimeSpan.FromMilliseconds(5),
                identity: command => ExecutionIdentity.ForTenant(command.TenantCode)));

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<TenantReportingCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Identities.Should().OnlyContain(identity => identity.TenantId == "acme");
    }

    private static ServiceProvider BuildProvider(IdentityLog log, Action<CommandModuleBuilder> register)
    {
        return new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands =>
            {
                commands.Register<TenantCommandHandler>();
                commands.Register<TenantReportingCommandHandler>();
                register(commands);
            });
        }).AddSingleton(log).BuildServiceProvider();
    }

    /// <summary>
    ///     Records the identities handlers observe and lets a test wait for a number of sends.
    /// </summary>
    public sealed class IdentityLog : IDisposable
    {
        /// <summary>
        ///     Signals each time a send completes so a waiter can re-check the count.
        /// </summary>
        private readonly SemaphoreSlim _signal = new(0);

        /// <summary>
        ///     The identities observed so far, in the order they were recorded.
        /// </summary>
        private readonly List<ExecutionIdentity> _identities = [];

        /// <summary>
        ///     The number of sends recorded so far.
        /// </summary>
        private int _count;

        /// <summary>
        ///     Gets the number of sends recorded so far.
        /// </summary>
        public int Count => Volatile.Read(ref _count);

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
        ///     Records the identity one send observed.
        /// </summary>
        /// <param name="identity">The observed identity.</param>
        public void Record(ExecutionIdentity identity)
        {
            lock (_identities)
            {
                _identities.Add(identity);
            }

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
    ///     A command carrying the tenant its identity is computed from.
    /// </summary>
    public sealed class TenantCommand : ICommand
    {
        public string TenantCode { get; init; } = "";
    }

    /// <summary>
    ///     A result-bearing command carrying the tenant its identity is computed from.
    /// </summary>
    public sealed class TenantReportingCommand : ICommand<TenantReport>
    {
        public string TenantCode { get; init; } = "";
    }

    /// <summary>
    ///     The result the reporting test command produces.
    /// </summary>
    public sealed record TenantReport(string TenantCode);

    private sealed class TenantCommandHandler : ICommandHandler<TenantCommand>
    {
        private readonly IdentityLog _log;

        public TenantCommandHandler(IdentityLog log)
        {
            _log = log;
        }

        public Task HandleAsync(TenantCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(AmbientExecutionContext.Current.Identity);
            return Task.CompletedTask;
        }
    }

    private sealed class TenantReportingCommandHandler : ICommandHandler<TenantReportingCommand, TenantReport>
    {
        private readonly IdentityLog _log;

        public TenantReportingCommandHandler(IdentityLog log)
        {
            _log = log;
        }

        public Task<TenantReport> HandleAsync(TenantReportingCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record(AmbientExecutionContext.Current.Identity);
            return Task.FromResult(new TenantReport(message.TenantCode));
        }
    }
}
