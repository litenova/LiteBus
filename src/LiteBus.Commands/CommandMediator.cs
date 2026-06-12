using System;
using System.Collections.Generic;
using System.Linq;
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
            Tags = ResolveTags(commandMediationSettings),
            Items = commandMediationSettings.Items,
            HandlerPredicate = ResolveHandlerPredicate(commandMediationSettings)
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
            Tags = ResolveTags(commandMediationSettings),
            Items = commandMediationSettings.Items,
            HandlerPredicate = ResolveHandlerPredicate(commandMediationSettings)
        };

        return _messageMediator.Mediate(command, request, cancellationToken);
    }

    /// <summary>
    ///     Resolves mediation tags from routing settings with legacy filter fallback.
    /// </summary>
    /// <param name="settings">The command mediation settings supplied by the caller.</param>
    /// <returns>The tag collection applied during mediation.</returns>
    private static IEnumerable<string> ResolveTags(CommandMediationSettings settings)
    {
        var routingTags = settings.Routing.Tags.ToList();
        return routingTags.Count > 0 ? routingTags : settings.Filters.Tags;
    }

    /// <summary>
    ///     Resolves the handler predicate from routing settings with legacy filter fallback.
    /// </summary>
    /// <param name="settings">The command mediation settings supplied by the caller.</param>
    /// <returns>The predicate applied after tag filtering.</returns>
    private static Func<IHandlerDescriptor, bool> ResolveHandlerPredicate(CommandMediationSettings settings)
    {
        return descriptor => settings.Routing.HandlerPredicate(descriptor) && settings.Filters.HandlerPredicate(descriptor);
    }
}
