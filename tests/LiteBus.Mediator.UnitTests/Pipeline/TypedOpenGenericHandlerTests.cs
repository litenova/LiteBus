using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that an open generic handler can take a second type parameter bound to the result type its message
///     declares, so generic cross-cutting code reaches the typed contract.
/// </summary>
/// <remarks>
///     Before this, a generic post-handler had to fall back to the untyped contract and an <c>object?</c> result, which
///     made the typed half of the API unreachable from generic code.
/// </remarks>
[Collection("Sequential")]
public sealed class TypedOpenGenericHandlerTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering the typed generic post-handler over both commands.
    /// </summary>
    /// <param name="observed">The recorder shared with the handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(TypedResultRecorder observed)
    {
        var services = new ServiceCollection();
        services.AddSingleton(observed);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<IssueTicketCommand>();
                    builder.Register<IssueTicketCommandHandler>();
                    builder.Register<VoidTicketCommand>();
                    builder.Register<VoidTicketCommandHandler>();
                    builder.Register(typeof(TypedResultPostHandler<,>));
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task A_generic_post_handler_receives_the_declared_result_typed()
    {
        var observed = new TypedResultRecorder();
        var provider = BuildProvider(observed);

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new IssueTicketCommand()).ConfigureAwait(false);

        result.Number.Should().Be("T-1");

        // The handler read the result through TTicketResult rather than casting an object?, which is the whole point.
        observed.Seen.Should().Equal("TicketNumber:T-1");
    }

    [Fact]
    public async Task One_registration_covers_every_message_that_declares_a_result()
    {
        var observed = new TypedResultRecorder();
        var provider = BuildProvider(observed);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new IssueTicketCommand()).ConfigureAwait(false);
        await mediator.SendAsync(new VoidTicketCommand()).ConfigureAwait(false);

        observed.Seen.Should().Equal("TicketNumber:T-1", "Boolean:True");
    }

    [Fact]
    public void An_arity_two_handler_whose_second_parameter_binds_to_nothing_is_still_rejected()
    {
        var act = () => new ServiceCollection()
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder =>
                {
                    builder.Register<IssueTicketCommand>();
                    builder.Register<IssueTicketCommandHandler>();
                    builder.Register(typeof(ContextualPostHandler<,>));
                });
            })
            .BuildServiceProvider();

        // TContext is the handler's own invention. The registry closes by position and has nothing to put there.
        act.Should().Throw<UnsupportedOpenGenericHandlerException>()
            .Which.GenericParameterCount.Should().Be(2);
    }

    [Fact]
    public async Task A_message_declaring_no_result_is_skipped_rather_than_failing_composition()
    {
        var observed = new TypedResultRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(observed);

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder =>
                {
                    builder.Register<PurgeTicketsCommand>();
                    builder.Register<PurgeTicketsCommandHandler>();
                    builder.Register(typeof(TypedResultPostHandler<,>));
                });
            })
            .BuildServiceProvider();

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new PurgeTicketsCommand()).ConfigureAwait(false);

        // A generic handler covers the messages it fits. A void command has no result to bind, so the handler simply
        // does not apply to it, the same silence a constraint mismatch produces.
        observed.Seen.Should().BeEmpty();
    }

    [Fact]
    public async Task One_generic_shortcut_answers_every_command_that_declares_a_result()
    {
        var cache = new TypedResultCache();
        cache.Seed(typeof(IssueTicketCommand), new TicketNumber("cached"));
        cache.Seed(typeof(VoidTicketCommand), true);

        var services = new ServiceCollection();
        services.AddSingleton(cache);
        services.AddSingleton(new TypedResultRecorder());

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder =>
                {
                    builder.Register<IssueTicketCommand>();
                    builder.Register<IssueTicketCommandHandler>();
                    builder.Register<VoidTicketCommand>();
                    builder.Register<VoidTicketCommandHandler>();
                    builder.Register(typeof(TypedResultShortcut<,>));
                });
            })
            .BuildServiceProvider();

        var mediator = provider.GetRequiredService<ICommandMediator>();

        // One registration covers both result shapes, which is the whole point: a generic caching shortcut is
        // inexpressible through the untyped contract because that one carries no result.
        var ticket = await mediator.SendAsync(new IssueTicketCommand()).ConfigureAwait(false);
        var voided = await mediator.SendAsync(new VoidTicketCommand()).ConfigureAwait(false);

        ticket.Number.Should().Be("cached");
        voided.Should().BeTrue();
    }

    [Fact]
    public async Task A_generic_shortcut_lets_a_command_it_holds_no_answer_for_proceed()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new TypedResultCache());
        services.AddSingleton(new TypedResultRecorder());

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder =>
                {
                    builder.Register<IssueTicketCommand>();
                    builder.Register<IssueTicketCommandHandler>();
                    builder.Register(typeof(TypedResultShortcut<,>));
                });
            })
            .BuildServiceProvider();

        var ticket = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new IssueTicketCommand()).ConfigureAwait(false);

        ticket.Number.Should().Be("T-1");
    }

    [Fact]
    public async Task One_generic_refusal_mapper_covers_every_result_type()
    {
        var refusals = new RefusalRecorder();
        var services = new ServiceCollection();
        services.AddSingleton(refusals);

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });
                registry.AddCommands(builder =>
                {
                    builder.Register<DeniedTicketCommand>();
                    builder.Register<DeniedTicketCommandHandler>();
                    builder.Register<DeniedTicketCommandGuard>();
                    builder.Register(typeof(TypedResultRefusalMapper<,>));
                });
            })
            .BuildServiceProvider();

        // A mapper registered against ICommand covers one result type. Only a generic one expresses "map every
        // denial onto this shape", and the caller receives a value rather than an exception.
        var refused = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new DeniedTicketCommand()).ConfigureAwait(false);

        refused.Number.Should().Be("refused");
        refusals.Seen.Should().Equal("DeniedTicketCommand:Denied:NOT_PERMITTED");
    }
}
