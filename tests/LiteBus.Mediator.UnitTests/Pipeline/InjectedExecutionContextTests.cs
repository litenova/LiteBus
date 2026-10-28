using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies that <see cref="IExecutionContext" /> is resolvable from the dispatch scope, so a handler declares it
///     as a constructor dependency instead of reaching for <see cref="AmbientExecutionContext" />.
/// </summary>
/// <remarks>
///     The ambient static still works and remains the way to reach the context from code that runs outside dependency
///     injection. What it cannot do is put the dependency in a type signature, which is what makes a handler that needs
///     mediation state testable without an ambient scope.
/// </remarks>
[Collection("Sequential")]
public sealed class InjectedExecutionContextTests : LiteBusTestBase
{
    [Fact]
    public async Task A_handler_receives_the_context_of_the_mediation_it_runs_in()
    {
        var observed = new InjectedContextRecorder();
        var provider = BuildProvider(observed);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new InjectedContextCommand { Note = "first" }).ConfigureAwait(false);

        observed.Notes.Should().Equal("first");
    }

    [Fact]
    public async Task Each_mediation_resolves_its_own_context()
    {
        var observed = new InjectedContextRecorder();
        var provider = BuildProvider(observed);
        var mediator = provider.GetRequiredService<ICommandMediator>();

        await mediator.SendAsync(new InjectedContextCommand { Note = "first" }).ConfigureAwait(false);
        await mediator.SendAsync(new InjectedContextCommand { Note = "second" }).ConfigureAwait(false);

        // One dispatch scope per mediation, so the container's per-scope cache never hands the second mediation the
        // first mediation's context.
        observed.Notes.Should().Equal("first", "second");
        observed.DistinctContexts.Should().Be(2);
    }

    [Fact]
    public async Task The_injected_context_is_the_same_instance_the_ambient_static_returns()
    {
        var observed = new InjectedContextRecorder();
        var provider = BuildProvider(observed);

        await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new InjectedContextCommand { Note = "same" }).ConfigureAwait(false);

        observed.MatchedAmbient.Should().BeTrue();
    }

    [Fact]
    public void Resolving_the_context_outside_a_mediation_reports_that_there_is_none()
    {
        var provider = BuildProvider(new InjectedContextRecorder());

        using var scope = provider.CreateScope();
        var act = () => scope.ServiceProvider.GetRequiredService<IExecutionContext>();

        // The same failure AmbientExecutionContext.Current already reports, because there is no mediation whose
        // context this could be.
        act.Should().Throw<NoExecutionContextException>();
    }

    /// <summary>
    ///     Builds a provider registering the command, its handler, and the shared recorder.
    /// </summary>
    /// <param name="recorder">The recorder shared with the handler.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(InjectedContextRecorder recorder)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register<InjectedContextCommand>();
                    builder.Register<InjectedContextCommandHandler>();
                });
            })
            .BuildServiceProvider();
    }
}
