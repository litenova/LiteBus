using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Covers that the recurring loop fails at startup when the command mediator is missing.
/// </summary>
public sealed class RecurringCommandMediatorStartupTests
{
    [Fact]
    public async Task ExecuteAsync_without_mediator_throws_at_start()
    {
        await using var emptyProvider = new ServiceCollection().BuildServiceProvider();
        var scopeFactory = new NullMediatorScopeFactory(emptyProvider);
        var declaration = new RecurringCommandDeclaration<StartupProbeCommand>(
            static () => new StartupProbeCommand(),
            new RecurringCommandHostOptions { Interval = TimeSpan.FromMilliseconds(5) });
        var service = new RecurringCommandBackgroundService<StartupProbeCommand>(
            declaration,
            scopeFactory,
            TimeProvider.System);

        var act = () => service.ExecuteAsync(CancellationToken.None);

        await act.Should().ThrowAsync<LiteBusDependencyResolutionException>().ConfigureAwait(false);
    }

    public sealed class StartupProbeCommand : ICommand;

    private sealed class NullMediatorScope : IMessageDispatchScope
    {
        private readonly IServiceProvider _provider;

        public NullMediatorScope(IServiceProvider provider)
        {
            _provider = provider;
        }

        public IServiceProvider ServiceProvider => _provider;

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }

    private sealed class NullMediatorScopeFactory : IMessageDispatchScopeFactory
    {
        private readonly IServiceProvider _provider;

        public NullMediatorScopeFactory(IServiceProvider provider)
        {
            _provider = provider;
        }

        public IMessageDispatchScope CreateScope()
        {
            return new NullMediatorScope(_provider);
        }
    }
}
