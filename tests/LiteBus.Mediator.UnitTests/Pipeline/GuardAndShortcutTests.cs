using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Pipeline;

/// <summary>
///     Verifies the two decisions that can end a mediation before the main handler, how each is reported, the order the
///     framework fixes between them, and post-handler suppression.
/// </summary>
/// <remarks>
///     The invariants under test are that the three endings stay distinct and that a shortcut can never answer ahead of
///     a guard. A refusal is a denial, an early answer is a success because nothing was refused, and suppressing
///     post-handlers after the work happened is also a success. Collapsing any pair would put a false entry in an audit
///     trail; letting a shortcut run first would let a cached answer reach a caller a guard would have refused.
/// </remarks>
[Collection("Sequential")]
public sealed class GuardAndShortcutTests : LiteBusTestBase
{
    [Fact]
    public async Task An_answering_shortcut_skips_the_main_handler_and_reports_Answered()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);
        var command = new GatedCommand { Decision = StageDecision.Answer };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeFalse();
        command.PostHandlerRan.Should().BeFalse();

        var observed = recorder.Observed.Single().Context;
        observed.Outcome.Should().Be(MediationOutcome.Answered);
        observed.Reason.Should().Be("already applied");
        observed.Faulted.Should().BeFalse();
    }

    [Fact]
    public async Task A_denial_reports_Denied_and_reaches_the_caller_as_an_exception()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);
        var command = new GatedCommand { Decision = StageDecision.Deny };

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        var thrown = await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);
        thrown.Which.Reason.Should().Be("the caller may not do this");
        thrown.Which.MessageType.Should().Be(typeof(GatedCommand));

        command.HandlerRan.Should().BeFalse();

        var observed = recorder.Observed.Single().Context;
        observed.Outcome.Should().Be(MediationOutcome.Denied);
        observed.Reason.Should().Be("the caller may not do this");
    }

    [Fact]
    public async Task A_denial_is_a_decision_so_error_handlers_do_not_see_it()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(GatedCommandErrorHandler));
        var command = new GatedCommand { Decision = StageDecision.Deny };

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        // An error handler exists to recover from faults. Letting it see a refusal would let it undo the refusal.
        command.ErrorHandlerRan.Should().BeFalse();
        recorder.Observed.Single().Context.Faulted.Should().BeFalse();
    }

    [Fact]
    public async Task Allowing_and_answering_nothing_lets_the_pipeline_proceed()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder);
        var command = new GatedCommand();

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
        command.PostHandlerRan.Should().BeTrue();
        recorder.Observed.Single().Context.Outcome.Should().Be(MediationOutcome.Succeeded);
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
        recorder.Observed.Single().Context.Outcome.Should().Be(MediationOutcome.Succeeded);
    }

    [Fact]
    public async Task A_guard_runs_before_a_shortcut_even_when_scope_and_priority_favour_the_shortcut()
    {
        var recorder = new StageOrderRecorder();
        var provider = BuildOrderedProvider(recorder, typeof(DirectRefusingGuard), typeof(IndirectAnsweringShortcut));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new OrderedCommand()).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>()
            .WithMessage("*the caller is not permitted*").ConfigureAwait(false);

        // The shortcut is indirect and has the default priority, so under one merged pre-handler stage it would have
        // answered first and handed a cached value to a caller the guard refuses. The fixed stage order prevents it.
        recorder.Observed.Should().ContainSingle().Which.Should().Be("guard");
    }

    [Fact]
    public async Task The_four_stages_run_as_guards_then_validators_then_shortcuts_then_pre_handlers()
    {
        var recorder = new StageOrderRecorder();
        var provider = BuildOrderedProvider(
            recorder,
            typeof(AllowingOrderedGuard),
            typeof(AllowingOrderedValidator),
            typeof(PassiveOrderedShortcut),
            typeof(OrderedCommandPreHandler));

        await provider.GetRequiredService<ICommandMediator>().SendAsync(new OrderedCommand()).ConfigureAwait(false);

        // The validator carries a lower priority number than the guard, so priority alone would run it first. The
        // framework fixes the stage order, and priority only orders handlers inside one stage.
        recorder.Observed.Should().Equal("guard", "validator", "shortcut", "pre-handler", "handler");
    }

    [Fact]
    public async Task An_answering_shortcut_supplies_the_result_the_caller_receives()
    {
        var provider = BuildResultProvider(typeof(CachedValueShortcut));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { Decision = StageDecision.Answer }).ConfigureAwait(false);

        result.Should().Be("from-cache");
    }

    [Fact]
    public async Task A_registered_mapper_hands_the_caller_a_refusal_value_instead_of_throwing()
    {
        var provider = BuildResultProvider(typeof(CodedRefusalGuard), typeof(CachedValueRefusalMapper));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { Decision = StageDecision.Deny }).ConfigureAwait(false);

        // The guard supplied only a reason and a code; the mapper decided what a refused caller receives.
        result.Should().Be("refused:NOT_OWNER");
    }

    [Fact]
    public async Task A_denial_throws_when_no_mapper_covers_the_command()
    {
        var provider = BuildResultProvider(typeof(CodedRefusalGuard));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { Decision = StageDecision.Deny }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>()
            .WithMessage("*not your order*").ConfigureAwait(false);
    }

    [Fact]
    public async Task The_untyped_guard_is_correct_for_a_command_that_produces_a_result()
    {
        var provider = BuildResultProvider(typeof(UntypedGuardOnResultCommand));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { Decision = StageDecision.Deny }).ConfigureAwait(false);

        // A refusal never owes the caller the value the handler would have produced, so this is a denial rather than
        // the configuration error the equivalent untyped shortcut produces.
        await act.Should().ThrowAsync<LiteBusMessageDeniedException>()
            .WithMessage("*refused by the untyped guard*").ConfigureAwait(false);
    }

    [Fact]
    public void An_untyped_shortcut_on_a_message_with_a_result_is_rejected_at_registration()
    {
        var register = () => BuildResultProvider(typeof(ResultlessShortcut));

        // Registration links the shortcut to a message whose main handler produces a string, which is enough to prove
        // the shortcut can never answer it. Waiting for dispatch would hide the mistake until the first cache hit.
        register.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*ResultlessShortcut*")
            .WithMessage("*IMessageShortcut<CachedValueCommand, String>*");
    }

    [Fact]
    public void An_untyped_shortcut_is_rejected_whichever_side_registers_first()
    {
        // A handler may be registered before or after the message it handles, and the registry commits after every
        // call, so the check has to fire from either direction.
        var shortcutFirst = () => BuildResultProviderInOrder(typeof(ResultlessShortcut), typeof(CachedValueCommandHandler));
        var handlerFirst = () => BuildResultProviderInOrder(typeof(CachedValueCommandHandler), typeof(ResultlessShortcut));

        shortcutFirst.Should().Throw<LiteBusConfigurationException>().WithMessage("*ResultlessShortcut*");
        handlerFirst.Should().Throw<LiteBusConfigurationException>().WithMessage("*ResultlessShortcut*");
    }

    [Fact]
    public void An_untyped_shortcut_on_a_message_without_a_result_is_accepted()
    {
        // GatedCommand produces nothing, so the untyped contract is the only one that fits it. The check must not
        // catch the case the contract exists for.
        var register = () => BuildProvider(new CompletionRecorder());

        register.Should().NotThrow();
    }

    [Fact]
    public async Task A_global_untyped_shortcut_reaching_a_message_with_a_result_reports_the_shortcut_by_name()
    {
        // Registered against ICommand, so it legitimately covers the result-less commands beneath it and registration
        // cannot prove it wrong. When it does answer a command that produces a result, the error names it rather than
        // leaving the reader to guess which of the globally registered shortcuts was responsible.
        var provider = BuildResultProvider(typeof(GlobalUntypedShortcut));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new CachedValueCommand { Decision = StageDecision.Answer }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusConfigurationException>()
            .WithMessage("*GlobalUntypedShortcut*")
            .WithMessage("*IMessageShortcut<CachedValueCommand, String>*").ConfigureAwait(false);
    }

    [Fact]
    public async Task Pre_handlers_after_a_stopping_decision_do_not_run()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildProvider(recorder, typeof(NeverReachedPreHandler));
        var command = new GatedCommand { Decision = StageDecision.Answer };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.LatePreHandlerRan.Should().BeFalse();
    }

    /// <summary>
    ///     Builds a provider registering the guard and shortcut test types for the command with no result.
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
                    builder.Register(typeof(GatedCommandGuard));
                    builder.Register(typeof(GatedCommandShortcut));
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
    ///     Builds a provider for the command that produces a result, with one decision handler under test.
    /// </summary>
    /// <param name="decisionTypes">The guards, shortcuts, or refusal mappers to register.</param>
    /// <returns>The configured service provider.</returns>
    /// <summary>
    ///     Builds a provider registering exactly the given types, in the given order, plus the message they concern.
    /// </summary>
    /// <param name="types">The handler types to register, in registration order.</param>
    /// <returns>The built provider.</returns>
    /// <remarks>
    ///     Separate from <see cref="BuildResultProvider" /> because that helper always registers the main handler
    ///     first, which would only ever exercise one of the two orders the registry has to handle.
    /// </remarks>
    private static ServiceProvider BuildResultProviderInOrder(params Type[] types)
    {
        var services = new ServiceCollection();

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(CachedValueCommand));

                    foreach (var type in types)
                    {
                        builder.Register(type);
                    }
                });
            })
            .BuildServiceProvider();
    }

    private static ServiceProvider BuildResultProvider(params Type[] decisionTypes)
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

                    foreach (var decisionType in decisionTypes)
                    {
                        builder.Register(decisionType);
                    }
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider for the command used to observe stage ordering.
    /// </summary>
    /// <param name="recorder">The recorder shared with the registered stages.</param>
    /// <param name="stageTypes">The stage handlers to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildOrderedProvider(StageOrderRecorder recorder, params Type[] stageTypes)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(OrderedCommand));
                    builder.Register(typeof(OrderedCommandHandler));

                    foreach (var stageType in stageTypes)
                    {
                        builder.Register(stageType);
                    }
                });
            })
            .BuildServiceProvider();
    }
}
