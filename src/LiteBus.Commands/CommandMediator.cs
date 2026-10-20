using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
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
    ///     Caches the non-generic send path per command runtime type, so the result-contract scan and the generic
    ///     method closure happen once per type rather than once per call.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<CommandMediator, ICommand, CommandMediationSettings, CancellationToken, Task>> SendDispatchers = new();

    /// <summary>
    ///     Caches the non-generic try-send path per command runtime type, so the result-contract scan and the generic
    ///     method closure happen once per type rather than once per call.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<CommandMediator, ICommand, CommandMediationSettings, CancellationToken, Task<MediationResult>>> TrySendDispatchers = new();

    /// <summary>
    ///     The open generic helper closed over each result-bearing command's declared result type for the
    ///     non-generic <see cref="SendAsync(ICommand, CommandMediationSettings, CancellationToken)" /> path.
    /// </summary>
    private static readonly MethodInfo SendResultBearingMethod = typeof(CommandMediator).GetMethod(nameof(SendResultBearingAsync), BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException($"Could not resolve {nameof(SendResultBearingAsync)}.");

    /// <summary>
    ///     The open generic helper closed over each result-bearing command's declared result type for the
    ///     non-generic <see cref="TrySendAsync(ICommand, CommandMediationSettings, CancellationToken)" /> path.
    /// </summary>
    private static readonly MethodInfo TrySendResultBearingMethod = typeof(CommandMediator).GetMethod(nameof(TrySendResultBearingAsync), BindingFlags.NonPublic | BindingFlags.Static) ?? throw new InvalidOperationException($"Could not resolve {nameof(TrySendResultBearingAsync)}.");

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

        var dispatcher = SendDispatchers.GetOrAdd(command.GetType(), CreateSendDispatcher);

        return dispatcher(this, command, commandMediationSettings, cancellationToken);
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
    public Task<MediationResult> TrySendAsync(
        ICommand command,
        CommandMediationSettings? commandMediationSettings = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        commandMediationSettings ??= new CommandMediationSettings();

        var dispatcher = TrySendDispatchers.GetOrAdd(command.GetType(), CreateTrySendDispatcher);

        return dispatcher(this, command, commandMediationSettings, cancellationToken);
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
    ///     Sends a command that declares no single result type through the no-result strategy.
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <param name="commandMediationSettings">The settings controlling the mediation.</param>
    /// <param name="cancellationToken">The token cancelling the mediation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    private Task SendWithoutResultAsync(
        ICommand command,
        CommandMediationSettings commandMediationSettings,
        CancellationToken cancellationToken)
    {
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

    /// <summary>
    ///     Sends a result-bearing command through the generic overload and discards the typed result for the caller.
    /// </summary>
    /// <typeparam name="TCommandResult">The result type the command declares.</typeparam>
    /// <param name="mediator">The mediator running the send.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="commandMediationSettings">The settings controlling the mediation.</param>
    /// <param name="cancellationToken">The token cancelling the mediation.</param>
    /// <returns>The command result, which the non-generic caller awaits as a plain task.</returns>
    private static Task<TCommandResult> SendResultBearingAsync<TCommandResult>(
        CommandMediator mediator,
        ICommand command,
        CommandMediationSettings commandMediationSettings,
        CancellationToken cancellationToken)
    {
        return mediator.SendAsync((ICommand<TCommandResult>) command, commandMediationSettings, cancellationToken);
    }

    /// <summary>
    ///     Sends a command that declares no single result type through the no-result try-send path.
    /// </summary>
    /// <param name="command">The command to send.</param>
    /// <param name="commandMediationSettings">The settings controlling the mediation.</param>
    /// <param name="cancellationToken">The token cancelling the mediation.</param>
    /// <returns>The outcome, and the reason and code when a decision stopped the pipeline.</returns>
    private async Task<MediationResult> TrySendWithoutResultAsync(
        ICommand command,
        CommandMediationSettings commandMediationSettings,
        CancellationToken cancellationToken)
    {
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

    /// <summary>
    ///     Sends a result-bearing command through the generic try-send overload and drops the value, keeping the
    ///     outcome the caller branches on.
    /// </summary>
    /// <typeparam name="TCommandResult">The result type the command declares.</typeparam>
    /// <param name="mediator">The mediator running the send.</param>
    /// <param name="command">The command to send.</param>
    /// <param name="commandMediationSettings">The settings controlling the mediation.</param>
    /// <param name="cancellationToken">The token cancelling the mediation.</param>
    /// <returns>The outcome, and the reason and code when a decision stopped the pipeline.</returns>
    private static async Task<MediationResult> TrySendResultBearingAsync<TCommandResult>(
        CommandMediator mediator,
        ICommand command,
        CommandMediationSettings commandMediationSettings,
        CancellationToken cancellationToken)
    {
        var typed = await mediator.TrySendAsync((ICommand<TCommandResult>) command, commandMediationSettings, cancellationToken).ConfigureAwait(false);

        return typed.Outcome switch
        {
            MediationOutcome.Denied => MediationResult.Denied(typed.Reason, typed.Code),
            MediationOutcome.Invalid => MediationResult.Invalid(typed.Reason, typed.Code, typed.Failures),
            MediationOutcome.Answered => MediationResult.Answered(typed.Reason, typed.Code),
            _ => MediationResult.Succeeded(),
        };
    }

    /// <summary>
    ///     Builds the non-generic send delegate for one command runtime type.
    /// </summary>
    /// <param name="commandType">The command runtime type the delegate is built for.</param>
    /// <returns>The delegate that sends commands of that type.</returns>
    /// <remarks>
    ///     A command whose runtime type implements several <see cref="ICommand{TCommandResult}" /> contracts keeps
    ///     the no-result path: there is no single result type to close the generic overload over, so the pipeline
    ///     runs without a result rather than guessing which contract the caller meant.
    /// </remarks>
    private static Func<CommandMediator, ICommand, CommandMediationSettings, CancellationToken, Task> CreateSendDispatcher(Type commandType)
    {
        var resultType = FindSingleResultType(commandType);

        if (resultType is null)
        {
            return static (mediator, command, settings, cancellationToken) => mediator.SendWithoutResultAsync(command, settings, cancellationToken);
        }

        return SendResultBearingMethod.MakeGenericMethod(resultType).CreateDelegate<Func<CommandMediator, ICommand, CommandMediationSettings, CancellationToken, Task>>();
    }

    /// <summary>
    ///     Builds the non-generic try-send delegate for one command runtime type.
    /// </summary>
    /// <param name="commandType">The command runtime type the delegate is built for.</param>
    /// <returns>The delegate that sends commands of that type and reports how the mediation ended.</returns>
    /// <remarks>
    ///     A command whose runtime type implements several <see cref="ICommand{TCommandResult}" /> contracts keeps
    ///     the no-result path: there is no single result type to close the generic overload over, so the pipeline
    ///     runs without a result rather than guessing which contract the caller meant.
    /// </remarks>
    private static Func<CommandMediator, ICommand, CommandMediationSettings, CancellationToken, Task<MediationResult>> CreateTrySendDispatcher(Type commandType)
    {
        var resultType = FindSingleResultType(commandType);

        if (resultType is null)
        {
            return static (mediator, command, settings, cancellationToken) => mediator.TrySendWithoutResultAsync(command, settings, cancellationToken);
        }

        return TrySendResultBearingMethod.MakeGenericMethod(resultType).CreateDelegate<Func<CommandMediator, ICommand, CommandMediationSettings, CancellationToken, Task<MediationResult>>>();
    }

    /// <summary>
    ///     Finds the single result type a command runtime type declares through <see cref="ICommand{TCommandResult}" />.
    /// </summary>
    /// <param name="commandType">The command runtime type to inspect.</param>
    /// <returns>The declared result type, or <see langword="null" /> when the command declares none or several.</returns>
    private static Type? FindSingleResultType(Type commandType)
    {
        Type? resultType = null;

        foreach (var contract in commandType.GetInterfaces())
        {
            if (!contract.IsGenericType || contract.GetGenericTypeDefinition() != typeof(ICommand<>))
            {
                continue;
            }

            if (resultType is not null)
            {
                return null;
            }

            resultType = contract.GetGenericArguments()[0];
        }

        return resultType;
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
