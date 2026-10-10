using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Queries;
using LiteBus.Queries.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that a pipeline handler written against the messaging-level contract registers on an axis when its
///     message type is constrained to that axis.
/// </summary>
/// <remarks>
///     The axis builders used to accept only their own contracts, so a guard implementing <c>IMessageGuard</c> was
///     refused and had to be duplicated per axis. Duplicating authorization code means one copy gets the next fix.
/// </remarks>
[Collection("Sequential")]
public sealed class CrossAxisHandlerTests : LiteBusTestBase
{
    [Fact]
    public async Task A_messaging_level_guard_constrained_to_commands_runs_for_commands()
    {
        var recorder = new CrossAxisRecorder();
        var provider = BuildProvider(recorder);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ApproveLeaveCommand()).ConfigureAwait(false);

        recorder.Seen.Should().Equal(nameof(ApproveLeaveCommand));
    }

    [Fact]
    public async Task The_same_shape_constrained_to_queries_runs_for_queries()
    {
        var recorder = new CrossAxisRecorder();
        var provider = BuildProvider(recorder);

        var result = await provider.GetRequiredService<IQueryMediator>()
            .QueryAsync(new ListLeaveQuery()).ConfigureAwait(false);

        result.Should().Be(3);
        recorder.Seen.Should().Equal(nameof(ListLeaveQuery));
    }

    [Fact]
    public async Task A_messaging_level_guard_denies_exactly_as_an_axis_guard_does()
    {
        var recorder = new CrossAxisRecorder { DenyFor = nameof(ApproveLeaveCommand) };
        var provider = BuildProvider(recorder);

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new ApproveLeaveCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
    }

    [Fact]
    public void A_messaging_level_guard_with_no_axis_constraint_is_still_refused()
    {
        var act = () => new ServiceCollection()
            .AddSingleton(new CrossAxisRecorder())
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder =>
                {
                    builder.Register<ApproveLeaveCommand>();
                    builder.Register<ApproveLeaveCommandHandler>();
                    builder.Register(typeof(UnconstrainedGuard<>));
                });
            });

        // Nothing says which axis it is for, so accepting it on the command builder would silently close it over
        // every command while the author believed they were registering something narrower.
        act.Should().Throw<LiteBusNotSupportedException>()
            .WithMessage("*UnconstrainedGuard*");
    }

    /// <summary>
    ///     Builds a provider registering the shared guard on both axes.
    /// </summary>
    /// <param name="recorder">The recorder shared with the guards.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(CrossAxisRecorder recorder)
    {
        return new ServiceCollection()
            .AddSingleton(recorder)
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<ApproveLeaveCommand>();
                    builder.Register<ApproveLeaveCommandHandler>();
                    builder.Register(typeof(SharedCommandGuard<>));
                });

                registry.AddQueries(builder =>
                {
                    builder.Register<ListLeaveQuery>();
                    builder.Register<ListLeaveQueryHandler>();
                    builder.Register(typeof(SharedQueryGuard<>));
                });
            })
            .BuildServiceProvider();
    }
}
