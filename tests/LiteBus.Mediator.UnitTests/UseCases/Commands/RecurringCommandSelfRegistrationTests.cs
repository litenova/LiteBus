using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Runtime.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Covers that AddRecurring registers its command and rejects a command with two result contracts.
/// </summary>
public sealed class RecurringCommandSelfRegistrationTests
{
    [Fact]
    public async Task AddRecurring_without_explicit_command_registration_sends_on_first_pass()
    {
        var log = new SelfSufficientLog();
        await using var serviceProvider = new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<SelfSufficientHandler>()
                .AddRecurring(() => new SelfSufficientCommand(), host => host.Interval = TimeSpan.FromMilliseconds(5)));
        }).AddSingleton(log).BuildServiceProvider();

        var registry = serviceProvider.GetRequiredService<IMessageRegistry>();
        registry.Find(typeof(SelfSufficientCommand)).Should().NotBeNull();

        var service = serviceProvider.GetRequiredService<RecurringCommandBackgroundService<SelfSufficientCommand>>();
        using var cancellation = new CancellationTokenSource();

        var run = service.ExecuteAsync(cancellation.Token);
        await log.WaitForSendsAsync(1).ConfigureAwait(false);
        await cancellation.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);

        log.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public void AddRecurring_with_two_result_contracts_fails_at_composition()
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .AddRecurring(() => new TwoContractCommand()));
        });

        act.Should().Throw<LiteBusConfigurationException>();
    }

    /// <summary>
    ///     Counts sends and lets a test wait for a number of them without racing a fixed delay.
    /// </summary>
    public sealed class SelfSufficientLog
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

    public sealed class SelfSufficientCommand : ICommand;

    private sealed class SelfSufficientHandler : ICommandHandler<SelfSufficientCommand>
    {
        private readonly SelfSufficientLog _log;

        public SelfSufficientHandler(SelfSufficientLog log)
        {
            _log = log;
        }

        public Task HandleAsync(SelfSufficientCommand message, CancellationToken cancellationToken = default)
        {
            _log.Record();
            return Task.CompletedTask;
        }
    }

    public sealed class TwoContractCommand : ICommand<int>, ICommand<string>;
}
