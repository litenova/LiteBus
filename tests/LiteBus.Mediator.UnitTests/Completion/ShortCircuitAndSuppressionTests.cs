using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     Verifies short-circuiting through <see cref="PipelineDirective" /> and post-handler suppression, including the
///     invariant that separates them: an aborted mediation means the main handler never ran, while suppression after
///     the work happened still reports success.
/// </summary>
[Collection("Sequential")]
public sealed class ShortCircuitAndSuppressionTests : LiteBusTestBase
{
    /// <summary>
    ///     Builds a provider registering only the short-circuit test types.
    /// </summary>
    /// <param name="recorder">The recorder shared with the completion handler.</param>
    /// <param name="extraTypes">Additional types to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildProvider(CompletionRecorder recorder, params Type[] extraTypes)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(GatedCommand));
                    builder.Register(typeof(GatedCommandHandler));
                    builder.Register(typeof(GatedCommandPostHandler));
                    builder.Register(typeof(GatedCommandGate));
                    builder.Register(typeof(DirectCompletionHandlerForGated));

                    foreach (var extra in extraTypes)
                    {
                        builder.Register(extra);
                    }
                });
            })
            .BuildServiceProvider();
    }

    [Fact]
    public async Task A_short_circuit_skips_the_main_handler_and_reports_Aborted()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);
        var command = new GatedCommand { ShouldShortCircuit = true };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeFalse();
        command.PostHandlerRan.Should().BeFalse();

        var observed = recorder.Observed.Single().Context;
        observed.Outcome.Should().Be(MessageOutcome.Aborted);
        observed.AbortReason.Should().Be("gate closed");
    }

    [Fact]
    public async Task A_continue_directive_lets_the_pipeline_proceed()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);
        var command = new GatedCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        command.PostHandlerRan.Should().BeTrue();
        recorder.Observed.Single().Context.Outcome.Should().Be(MessageOutcome.Succeeded);
    }

    [Fact]
    public async Task Suppressing_post_handlers_still_reports_Succeeded()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);
        var command = new GatedCommand { ShouldSuppressPostHandlers = true };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        command.PostHandlerRan.Should().BeFalse();

        // The work happened, so this is not a denial. Reporting Aborted here would tell an audit trail
        // that a command was refused when it actually took effect.
        recorder.Observed.Single().Context.Outcome.Should().Be(MessageOutcome.Succeeded);
    }

    [Fact]
    public async Task A_short_circuit_supplies_the_result_the_caller_receives()
    {
        var services = new ServiceCollection();

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(CachedValueCommand));
                    builder.Register(typeof(CachedValueCommandHandler));
                    builder.Register(typeof(CachedValueGate));
                });
            })
            .BuildServiceProvider();

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { ShouldShortCircuit = true }).ConfigureAwait(false);

        result.Should().Be("from-cache");
    }

    [Fact]
    public async Task A_short_circuit_without_a_required_result_is_a_configuration_error()
    {
        var services = new ServiceCollection();

        var provider = services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(CachedValueCommand));
                    builder.Register(typeof(CachedValueCommandHandler));
                    builder.Register(typeof(ResultlessGate));
                });
            })
            .BuildServiceProvider();

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { ShouldShortCircuit = true }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusConfigurationException>()
            .WithMessage("*must supply a result of type*").ConfigureAwait(false);
    }

    [Fact]
    public async Task Pre_handlers_after_a_short_circuit_do_not_run()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(NeverReachedGate));
        var command = new GatedCommand { ShouldShortCircuit = true };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.SecondGateRan.Should().BeFalse();
    }
}
