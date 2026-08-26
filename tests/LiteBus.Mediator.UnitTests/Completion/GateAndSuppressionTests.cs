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
///     Verifies the two decisions a gate can take and how each is reported, plus post-handler suppression.
/// </summary>
/// <remarks>
///     The invariant under test is that the three endings stay distinct. A refusal is a denial, an early answer is a
///     success because nothing was refused, and suppressing post-handlers after the work happened is also a success.
///     Collapsing any pair of these would put a false entry in an audit trail.
/// </remarks>
[Collection("Sequential")]
public sealed class GateAndSuppressionTests : LiteBusTestBase
{
    [Fact]
    public async Task A_short_circuit_skips_the_main_handler_and_reports_ShortCircuited()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);
        var command = new GatedCommand { Decision = GateDecision.ShortCircuit };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeFalse();
        command.PostHandlerRan.Should().BeFalse();

        var observed = recorder.Observed.Single().Context;
        observed.Outcome.Should().Be(MessageOutcome.ShortCircuited);
        observed.Reason.Should().Be("already applied");
        observed.Faulted.Should().BeFalse();
    }

    [Fact]
    public async Task A_denial_reports_Denied_and_reaches_the_caller_as_an_exception()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);
        var command = new GatedCommand { Decision = GateDecision.Deny };

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Reason.Should().Be("the caller may not do this");
        thrown.Which.MessageType.Should().Be(typeof(GatedCommand));

        command.HandlerRan.Should().BeFalse();

        var observed = recorder.Observed.Single().Context;
        observed.Outcome.Should().Be(MessageOutcome.Denied);
        observed.Reason.Should().Be("the caller may not do this");
    }

    [Fact]
    public async Task A_denial_is_a_decision_so_error_handlers_do_not_see_it()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(GatedCommandErrorHandler));
        var command = new GatedCommand { Decision = GateDecision.Deny };

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        // An error handler exists to recover from faults. Letting it see a refusal would let it undo the refusal.
        command.ErrorHandlerRan.Should().BeFalse();
        recorder.Observed.Single().Context.Faulted.Should().BeFalse();
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

        // The work happened, so this is not a denial. Reporting Denied here would tell an audit trail
        // that a command was refused when it actually took effect.
        recorder.Observed.Single().Context.Outcome.Should().Be(MessageOutcome.Succeeded);
    }

    [Fact]
    public async Task A_short_circuit_supplies_the_result_the_caller_receives()
    {
        var provider = BuildResultProvider(typeof(CachedValueGate));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { Decision = GateDecision.ShortCircuit }).ConfigureAwait(false);

        result.Should().Be("from-cache");
    }

    [Fact]
    public async Task A_denial_may_hand_the_caller_a_refusal_value_instead_of_throwing()
    {
        var provider = BuildResultProvider(typeof(CachedValueGate));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { Decision = GateDecision.Deny }).ConfigureAwait(false);

        result.Should().Be("refused");
    }

    [Fact]
    public async Task A_denial_without_a_result_throws_even_when_the_command_produces_one()
    {
        var provider = BuildResultProvider(typeof(UnansweredDenialGate));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { Decision = GateDecision.Deny }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>()
            .WithMessage("*nothing to hand back*").ConfigureAwait(false);
    }

    [Fact]
    public async Task A_short_circuit_without_a_required_result_is_a_configuration_error()
    {
        var provider = BuildResultProvider(typeof(ResultlessGate));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { Decision = GateDecision.ShortCircuit }).ConfigureAwait(false);

        // The untyped gate cannot supply a result, which is why the typed one exists. The error names it.
        await act.Should().ThrowAsync<LiteBusConfigurationException>()
            .WithMessage("*IMessageGate<CachedValueCommand, String>*").ConfigureAwait(false);
    }

    [Fact]
    public async Task Pre_handlers_after_a_stopping_directive_do_not_run()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(NeverReachedGate));
        var command = new GatedCommand { Decision = GateDecision.ShortCircuit };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.SecondGateRan.Should().BeFalse();
    }

    /// <summary>
    ///     Builds a provider registering only the gate test types for the command with no result.
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

    /// <summary>
    ///     Builds a provider for the command that produces a result, with one gate under test.
    /// </summary>
    /// <param name="gateType">The gate to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildResultProvider(Type gateType)
    {
        var services = new ServiceCollection();

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(CachedValueCommand));
                    builder.Register(typeof(CachedValueCommandHandler));
                    builder.Register(gateType);
                });
            })
            .BuildServiceProvider();
    }
}
