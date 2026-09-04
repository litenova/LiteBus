using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.MediationStrategies;

namespace LiteBus.Commands;

/// <summary>
///     The primary implementation of <see cref="ICommandMediator" />. It orchestrates the command execution
///     pipeline for immediate, in-process command handling.
/// </summary>
public sealed class CommandMediator : ICommandMediator
{
    /// <summary>
    ///     Gets the core message mediator used to execute the command pipeline.
    /// </summary>
    private readonly IMessageMediator _messageMediator;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandMediator" /> class.
    /// </summary>
    /// <param name="messageMediator">The core message mediator for immediate command execution.</param>
    public CommandMediator(IMessageMediator messageMediator)
    {
        ArgumentNullException.ThrowIfNull(messageMediator);

        _messageMediator = messageMediator;
    }

    /// <inheritdoc />
    public Task SendAsync(ICommand command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        commandMediationSettings ??= new CommandMediationSettings();
        var mediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand>();
        var findStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        var request = new MessageMediationRequest<ICommand, Task>
        {
            MessageMediationStrategy = mediationStrategy,
            MessageResolveStrategy = findStrategy,
            Tags = commandMediationSettings.Routing.Tags,
            Items = commandMediationSettings.Items,
            HandlerPredicate = commandMediationSettings.Routing.HandlerPredicate
        };

        return _messageMediator.Mediate(command, request, cancellationToken);
    }

    /// <inheritdoc />
    public Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                          CommandMediationSettings? commandMediationSettings = null,
                                                          CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        commandMediationSettings ??= new CommandMediationSettings();
        var mediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand<TCommandResult>, TCommandResult>();
        var findStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        var request = new MessageMediationRequest<ICommand<TCommandResult>, Task<TCommandResult>>
        {
            MessageResolveStrategy = findStrategy,
            MessageMediationStrategy = mediationStrategy,
            Tags = commandMediationSettings.Routing.Tags,
            Items = commandMediationSettings.Items,
            HandlerPredicate = commandMediationSettings.Routing.HandlerPredicate
        };

        return _messageMediator.Mediate(command, request, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<MediationResult> TrySendAsync(
        ICommand command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        commandMediationSettings ??= new CommandMediationSettings();
        var capture = new MediationEndingCapture();

        var request = new MessageMediationRequest<ICommand, Task>
        {
            MessageMediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand>(),
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            Tags = commandMediationSettings.Routing.Tags,
            Items = WithEndingCapture(commandMediationSettings.Items, capture),
            HandlerPredicate = commandMediationSettings.Routing.HandlerPredicate
        };

        try
        {
            await _messageMediator.Mediate(command, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (MediationExceptionFilters.IsRefusal(exception))
        {
            // A refusal is the ending this method exists to hand back as a value. Anything else propagates: a fault
            // is not something a boundary should branch on.
            return MediationResultFactory.FromCapture(capture);
        }

        return MediationResultFactory.FromCapture(capture);
    }

    /// <inheritdoc />
    public async Task<MediationResult<TCommandResult>> TrySendAsync<TCommandResult>(
        ICommand<TCommandResult> command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        commandMediationSettings ??= new CommandMediationSettings();
        var capture = new MediationEndingCapture();

        var request = new MessageMediationRequest<ICommand<TCommandResult>, Task<TCommandResult>>
        {
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            MessageMediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand<TCommandResult>, TCommandResult>(),
            Tags = commandMediationSettings.Routing.Tags,
            Items = WithEndingCapture(commandMediationSettings.Items, capture),
            HandlerPredicate = commandMediationSettings.Routing.HandlerPredicate
        };

        try
        {
            var value = await _messageMediator.Mediate(command, request, cancellationToken).ConfigureAwait(false);

            // A registered refusal mapper returns a value rather than raising, so the outcome comes from the capture
            // and the mapped value comes back alongside it.
            return MediationResultFactory.FromCapture(capture, value, hasValue: true);
        }
        catch (Exception exception) when (MediationExceptionFilters.IsRefusal(exception))
        {
            return MediationResultFactory.FromCapture<TCommandResult>(capture, value: default, hasValue: false);
        }
    }

    /// <inheritdoc />
    public Task<MediationDecision> EvaluateAsync(
        ICommand command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        commandMediationSettings ??= new CommandMediationSettings();

        var request = new MessageMediationRequest<ICommand, Task<MediationDecision>>
        {
            MessageMediationStrategy = new DecisionEvaluationMediationStrategy<ICommand>(),
            MessageResolveStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy(),
            Tags = commandMediationSettings.Routing.Tags,
            Items = commandMediationSettings.Items,
            HandlerPredicate = commandMediationSettings.Routing.HandlerPredicate
        };

        return _messageMediator.Mediate(command, request, cancellationToken);
    }

    /// <summary>
    ///     Copies the caller's items and adds the ending capture the strategy fills in.
    /// </summary>
    /// <param name="items">The items the caller supplied.</param>
    /// <param name="capture">The capture to install.</param>
    /// <returns>The items to pass to the mediator.</returns>
    /// <remarks>
    ///     Copied rather than mutated, because the settings object may be reused across calls and installing a capture
    ///     into it would leave one mediation writing into a previous call's result.
    /// </remarks>
    private static Dictionary<string, object> WithEndingCapture(
        IDictionary<string, object> items,
        MediationEndingCapture capture)
    {
        return new Dictionary<string, object>(items, StringComparer.Ordinal)
        {
            [MediationEndingCapture.ItemKey] = capture
        };
    }
}
