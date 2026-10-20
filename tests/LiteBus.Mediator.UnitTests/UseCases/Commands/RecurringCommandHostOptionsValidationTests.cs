using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Commands.Recurring;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.UseCases.Commands;

/// <summary>
///     Covers that zero poll timing is refused at composition: a zero interval or error backoff would busy-spin the
///     loop instead of pacing passes.
/// </summary>
public sealed class RecurringCommandHostOptionsValidationTests
{
    [Fact]
    public void A_zero_interval_is_refused_at_composition()
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<ZeroProbeCommandHandler>()
                .AddRecurring(() => new ZeroProbeCommand(), host => host.Interval = TimeSpan.Zero));
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_zero_error_backoff_is_refused_at_composition()
    {
        var act = () => new ServiceCollection().AddLiteBus(registry =>
        {
            registry.AddMessaging(_ =>
            {
            });

            registry.AddCommands(commands => commands
                .Register<ZeroProbeCommandHandler>()
                .AddRecurring(() => new ZeroProbeCommand(), host => host.ErrorBackoff = TimeSpan.Zero));
        });

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    public sealed class ZeroProbeCommand : ICommand;

    private sealed class ZeroProbeCommandHandler : ICommandHandler<ZeroProbeCommand>
    {
        public Task HandleAsync(ZeroProbeCommand message, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
