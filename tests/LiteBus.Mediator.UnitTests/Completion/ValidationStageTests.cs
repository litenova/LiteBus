using LiteBus.Commands;
using LiteBus.Commands.Abstractions;
using LiteBus.Extensions.Microsoft.DependencyInjection;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace LiteBus.Mediator.UnitTests.Completion;

/// <summary>
///     Covers the validator stage, its position between guards and shortcuts, and refusal mapping.
/// </summary>
public sealed class ValidationStageTests
{
    [Fact]
    public async Task A_well_formed_command_reaches_the_handler()
    {
        var provider = BuildTransferProvider(
            typeof(TransferAmountValidator),
            typeof(TransferReferenceValidator));

        var command = new TransferCommand { Amount = 10m, Reference = "INV-1" };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        command.HandlerRan.Should().BeTrue();
    }

    [Fact]
    public async Task A_malformed_command_stops_before_the_handler_and_reports_Invalid()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildTransferProvider(
            recorder,
            typeof(TransferAmountValidator));

        var command = new TransferCommand { Amount = 0m, Reference = "INV-1" };

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageInvalidException>()
            .WithMessage("*the amount must be positive*").ConfigureAwait(false);

        command.HandlerRan.Should().BeFalse();
        recorder.Observed.Select(entry => entry.Context.Outcome).Should().Equal(MessageOutcome.Invalid);
    }

    [Fact]
    public async Task Every_validator_runs_so_the_caller_sees_all_failures_at_once()
    {
        var provider = BuildTransferProvider(
            typeof(TransferAmountValidator),
            typeof(TransferReferenceValidator));

        var command = new TransferCommand { Amount = 0m, Reference = null };

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        // The guard stage stops at the first refusal because one reason is enough. This stage does not, because a
        // caller fixing the command should not have to discover its problems one round trip at a time.
        var thrown = await act.Should().ThrowAsync<LiteBusMessageInvalidException>().ConfigureAwait(false);

        thrown.Which.Failures.Should().HaveCount(2);
        thrown.Which.Failures.Select(failure => failure.Code).Should().BeEquivalentTo("AMOUNT", "REFERENCE");
        thrown.Which.Failures.Select(failure => failure.Member)
            .Should().BeEquivalentTo(nameof(TransferCommand.Amount), nameof(TransferCommand.Reference));
    }

    [Fact]
    public async Task A_guard_refusal_stops_the_pipeline_before_any_validator_runs()
    {
        var provider = BuildTransferProvider(
            typeof(TransferPermissionGuard),
            typeof(TransferAmountValidator));

        var command = new TransferCommand { Amount = 0m, Reference = "INV-1", IsPermitted = false };

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        // An unauthorized caller learns nothing about the message contents, not even that they were malformed.
        command.StagesRun.Should().Equal("guard");
    }

    [Fact]
    public async Task The_stage_order_is_guard_then_validator_then_shortcut_then_pre_handler()
    {
        var provider = BuildTransferProvider(
            typeof(TransferPermissionGuard),
            typeof(TransferAmountValidator),
            typeof(TransferAlwaysAnsweringShortcut),
            typeof(TransferPreHandler));

        var command = new TransferCommand { Amount = 10m, Reference = "INV-1" };

        await provider.GetRequiredService<ICommandMediator>().SendAsync(command).ConfigureAwait(false);

        // The shortcut answers, so the pre-handler and the main handler never run.
        command.StagesRun.Should().Equal("guard", "amount-validator", "shortcut");
        command.HandlerRan.Should().BeFalse();
    }

    [Fact]
    public async Task A_lowest_priority_shortcut_cannot_answer_ahead_of_a_guard()
    {
        var provider = BuildTransferProvider(
            typeof(TransferPermissionGuard),
            typeof(TransferAlwaysAnsweringShortcut));

        var command = new TransferCommand { Amount = 10m, Reference = "INV-1", IsPermitted = false };

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        // The shortcut carries int.MinValue priority, which under priority ordering alone would put it first. The
        // framework fixes the stage order instead, so it never runs and cannot answer a refused caller.
        await act.Should().ThrowAsync<LiteBusMessageDeniedException>().ConfigureAwait(false);

        command.StagesRun.Should().Equal("guard");
    }

    [Fact]
    public async Task A_lowest_priority_shortcut_cannot_answer_a_malformed_command()
    {
        var provider = BuildTransferProvider(
            typeof(TransferAmountValidator),
            typeof(TransferAlwaysAnsweringShortcut));

        var command = new TransferCommand { Amount = -1m, Reference = "INV-1" };

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(command).ConfigureAwait(false);

        // A malformed message must not claim an idempotency key or collect a cached answer.
        await act.Should().ThrowAsync<LiteBusMessageInvalidException>().ConfigureAwait(false);

        command.StagesRun.Should().Equal("amount-validator");
    }

    [Fact]
    public async Task A_refusal_mapper_turns_a_denial_into_the_result_the_caller_expects()
    {
        var provider = BuildQuoteProvider(typeof(QuotePermissionGuard), typeof(QuoteRefusalMapper));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new QuoteCommand { IsPermitted = false }).ConfigureAwait(false);

        result.Should().Be("quote:Denied:NOT_PERMITTED");
    }

    [Fact]
    public async Task A_refusal_mapper_also_covers_a_validation_failure()
    {
        var provider = BuildQuoteProvider(typeof(QuoteSymbolValidator), typeof(QuoteRefusalMapper));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new QuoteCommand { Symbol = null }).ConfigureAwait(false);

        // One registration covers both refusal shapes, because the refusal carries which one it is.
        result.Should().Be("quote:Invalid:SYMBOL");
    }

    [Fact]
    public async Task A_mapper_registered_for_the_concrete_message_wins_over_one_registered_for_the_axis()
    {
        var provider = BuildQuoteProvider(
            typeof(QuotePermissionGuard),
            typeof(GlobalCommandRefusalMapper),
            typeof(QuoteRefusalMapper));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new QuoteCommand { IsPermitted = false }).ConfigureAwait(false);

        result.Should().Be("quote:Denied:NOT_PERMITTED");
    }

    [Fact]
    public async Task A_mapper_registered_for_the_axis_covers_a_message_with_no_mapper_of_its_own()
    {
        var provider = BuildQuoteProvider(typeof(QuotePermissionGuard), typeof(GlobalCommandRefusalMapper));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new QuoteCommand { IsPermitted = false }).ConfigureAwait(false);

        result.Should().Be("global:Denied:NOT_PERMITTED");
    }

    [Fact]
    public async Task A_mapped_refusal_still_reports_Denied_and_hands_the_value_to_the_completion_stage()
    {
        var recorder = new CompletionRecorder();
        var provider = BuildQuoteProvider(
            recorder,
            typeof(QuotePermissionGuard),
            typeof(QuoteRefusalMapper));

        var result = await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new QuoteCommand { IsPermitted = false }).ConfigureAwait(false);

        result.Should().Be("quote:Denied:NOT_PERMITTED");

        // Mapping decides what the caller receives; it does not turn a refusal into a success. An audit trail built on
        // the completion stage must still see the denial, and must see the value the caller actually got.
        var completion = recorder.Observed.Should().ContainSingle().Which.Context;
        completion.Outcome.Should().Be(MessageOutcome.Denied);
        completion.Reason.Should().Be("not permitted");
        completion.MessageResult.Should().Be("quote:Denied:NOT_PERMITTED");
        completion.Faulted.Should().BeFalse();
    }

    [Fact]
    public async Task Two_mappers_at_the_same_level_are_reported_rather_than_resolved_by_scanning_order()
    {
        var provider = BuildQuoteProvider(
            typeof(QuotePermissionGuard),
            typeof(QuoteRefusalMapper),
            typeof(DuplicateQuoteRefusalMapper));

        var act = async () => await provider.GetRequiredService<ICommandMediator>()
            .SendAsync(new QuoteCommand { IsPermitted = false }).ConfigureAwait(false);

        await act.Should().ThrowAsync<LiteBusConfigurationException>()
            .WithMessage("*more than one refusal mapper*").ConfigureAwait(false);
    }

    [Fact]
    public void A_type_carrying_only_a_pipeline_marker_is_reported_at_registration()
    {
        var services = new ServiceCollection();

        var act = () => services.AddLiteBus(registry =>
            registry.AddMessaging(builder => builder.Register(typeof(MarkerOnlyPreStageHandler))));

        // Every pipeline marker is memberless, so this type produces no descriptor. It would otherwise register
        // successfully as a message type and never run.
        act.Should().Throw<LiteBusConfigurationException>()
            .WithMessage("*exposes no contract that names a message type*");
    }

    [Fact]
    public void An_axis_module_reports_a_marker_only_type_as_not_belonging_to_that_axis()
    {
        var services = new ServiceCollection();

        var act = () => services.AddLiteBus(registry =>
        {
            registry.AddMessaging(_ => { });
            registry.AddCommands(builder => builder.Register(typeof(MarkerOnlyPreStageHandler)));
        });

        // The command module recognizes only command contracts, so it names the axis rather than the marker.
        act.Should().Throw<LiteBusNotSupportedException>()
            .WithMessage("*is not a command construct*");
    }

    /// <summary>
    ///     Builds a provider for <see cref="TransferCommand" /> with the given pre-stage handlers registered.
    /// </summary>
    /// <param name="stageTypes">The guards, validators, shortcuts, or pre-handlers to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildTransferProvider(params Type[] stageTypes)
    {
        return BuildTransferProvider(new CompletionRecorder(), stageTypes);
    }

    /// <summary>
    ///     Builds a provider for <see cref="TransferCommand" /> with a recorder observing the completion stage.
    /// </summary>
    /// <param name="recorder">The recorder registered for completion observation.</param>
    /// <param name="stageTypes">The guards, validators, shortcuts, or pre-handlers to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildTransferProvider(CompletionRecorder recorder, params Type[] stageTypes)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(TransferCommand));
                    builder.Register(typeof(TransferCommandHandler));
                    builder.Register(typeof(TransferCompletionHandler));

                    foreach (var stageType in stageTypes)
                    {
                        builder.Register(stageType);
                    }
                });
            })
            .BuildServiceProvider();
    }

    /// <summary>
    ///     Builds a provider for <see cref="QuoteCommand" /> with the given decisions and mappers registered.
    /// </summary>
    /// <param name="stageTypes">The guards, validators, or refusal mappers to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildQuoteProvider(params Type[] stageTypes)
    {
        return BuildQuoteProvider(new CompletionRecorder(), stageTypes);
    }

    /// <summary>
    ///     Builds a provider for <see cref="QuoteCommand" /> with a recorder observing the completion stage.
    /// </summary>
    /// <param name="recorder">The recorder registered for completion observation.</param>
    /// <param name="stageTypes">The guards, validators, or refusal mappers to register.</param>
    /// <returns>The configured service provider.</returns>
    private static ServiceProvider BuildQuoteProvider(CompletionRecorder recorder, params Type[] stageTypes)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recorder);

        return services
            .AddLiteBus(registry =>
            {
                registry.AddMessaging(_ => { });

                registry.AddCommands(builder =>
                {
                    builder.Register(typeof(QuoteCommand));
                    builder.Register(typeof(QuoteCommandHandler));
                    builder.Register(typeof(QuoteCompletionHandler));

                    foreach (var stageType in stageTypes)
                    {
                        builder.Register(stageType);
                    }
                });
            })
            .BuildServiceProvider();
    }
}
