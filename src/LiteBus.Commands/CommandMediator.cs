using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands;

/// <summary>
///     The primary implementation of <see cref="ICommandMediator" />. It orchestrates the command execution
///     pipeline for immediate, in-process command handling.
/// </summary>
public sealed class CommandMediator : ICommandMediator
{
    private readonly IMessageMediator _messageMediator;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandMediator" />.
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

        var options = new MediateOptions<ICommand, Task>
        {
            MessageMediationStrategy = mediationStrategy,
            MessageResolveStrategy = findStrategy,
            CancellationToken = cancellationToken,
            Tags = commandMediationSettings.Filters.Tags,
            Items = commandMediationSettings.Items
        };

        return _messageMediator.Mediate(command, options);
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

        var options = new MediateOptions<ICommand<TCommandResult>, Task<TCommandResult>>
        {
            MessageResolveStrategy = findStrategy,
            MessageMediationStrategy = mediationStrategy,
            CancellationToken = cancellationToken,
            Tags = commandMediationSettings.Filters.Tags,
            Items = commandMediationSettings.Items
        };

        return _messageMediator.Mediate(command, options);
    }
}