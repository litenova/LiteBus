using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Mediator.UnitTests.Completion;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that the named handler priorities delimit the bands their documentation describes, and that an
///     application handler placed in the band above the reserved ceiling runs before the unit-of-work commit.
/// </summary>
/// <remarks>
///     <see cref="HandlerPriorities.UnitOfWork" /> and <see cref="HandlerPriorities.ReservedCeiling" /> used to be the
///     same number, which left no position for application infrastructure that has to run after every LiteBus handler
///     and still inside the transaction. A handler registered there tied with the commit and the tie resolved by
///     registration sequence.
/// </remarks>
[Collection("Sequential")]
public sealed class HandlerPrioritiesTests : LiteBusTestBase
{
    [Fact]
    public void The_reserved_window_holds_every_LiteBus_priority_and_excludes_the_ceiling()
    {
        HandlerPriorities.Default.Should().BeLessThan(HandlerPriorities.ReservedFloor);

        HandlerPriorities.Persistence.Should().BeInRange(
            HandlerPriorities.ReservedFloor,
            HandlerPriorities.ReservedCeiling - 1);

        HandlerPriorities.Observability.Should().BeInRange(
            HandlerPriorities.ReservedFloor,
            HandlerPriorities.ReservedCeiling - 1);

        // Observation runs after persistence, so LiteBus finishes its durable writes before recording that they happened.
        HandlerPriorities.Persistence.Should().BeLessThan(HandlerPriorities.Observability);
    }

    [Fact]
    public void The_commit_position_sits_above_the_ceiling_leaving_a_band_between_them()
    {
        HandlerPriorities.UnitOfWork.Should().BeGreaterThan(HandlerPriorities.ReservedCeiling);
    }

    [Fact]
    public async Task A_handler_in_the_band_above_the_ceiling_runs_before_the_unit_of_work_commit()
    {
        var recorder = new CompletionRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(CompletionCommand));
                    builder.Register(typeof(CompletionCommandGuard));
                    builder.Register(typeof(CompletionCommandShortcut));
                    builder.Register(typeof(CompletionCommandHandler));

                    // Registered commit-first on purpose: priority has to decide the order, not registration sequence.
                    builder.Register(typeof(UnitOfWorkCompletionHandler));
                    builder.Register(typeof(InfrastructureCompletionHandler));
                });
            })
            .BuildServiceProvider();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CompletionCommand()).ConfigureAwait(false);

        recorder.Observed.Select(observation => observation.Handler)
            .Should().Equal("infrastructure", "unit-of-work");
    }
}
