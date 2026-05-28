using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands;

/// <summary>
///     The primary implementation of <see cref="ICommandMediator" />. It orchestrates the command execution
///     pipeline, including diverting commands to be stored in the inbox if they are marked for durable processing.
/// </summary>
public sealed class CommandMediator : ICommandMediator
{
    private readonly ICommandInbox? _commandInbox;
    private readonly ConcurrentDictionary<Type, bool> _inboxAttributeCache = new();
    private readonly IMessageMediator _messageMediator;

    /// <summary>
    ///     Initializes a new instance of the <see cref="CommandMediator" />.
    /// </summary>
    /// <param name="messageMediator">The core message mediator for immediate command execution.</param>
    /// <param name="commandInbox">The registered command inbox implementation. If null, the inbox feature is disabled.</param>
    public CommandMediator(IMessageMediator messageMediator, ICommandInbox? commandInbox = null)
    {
        ArgumentNullException.ThrowIfNull(messageMediator);

        _messageMediator = messageMediator;
        _commandInbox = commandInbox;
    }

    /// <inheritdoc />
    public Task SendAsync(ICommand command, CommandMediationSettings? commandMediationSettings = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var commandType = command.GetType();
        ThrowIfResultCommandIsInboxed(commandType);

        if (ShouldBeStoredInInbox(commandType, commandMediationSettings))
        {
            return GetRequiredCommandInbox(commandType).StoreAsync(command, cancellationToken);
        }

        // Proceed with immediate, in-process execution.
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

        var commandType = command.GetType();
        ThrowIfResultCommandIsInboxed(commandType);

        if (ShouldBeStoredInInbox(commandType, commandMediationSettings))
        {
            throw new ResultCommandInboxNotSupportedException(commandType);
        }

        // Proceed with immediate, in-process execution.
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

    /// <summary>
    ///     Determines if a command should be stored in the inbox for deferred processing.
    /// </summary>
    private bool ShouldBeStoredInInbox(Type commandType, CommandMediationSettings? settings)
    {
        ArgumentNullException.ThrowIfNull(commandType);

        if (IsInboxExecution(settings))
        {
            return false;
        }

        return HasStoreInInboxAttribute(commandType);
    }

    private bool HasStoreInInboxAttribute(Type commandType)
    {
        return _inboxAttributeCache.GetOrAdd(
            commandType,
            type => Attribute.GetCustomAttribute(type, typeof(StoreInInboxAttribute)) is not null);
    }

    private ICommandInbox GetRequiredCommandInbox(Type commandType)
    {
        if (_commandInbox is null)
        {
            throw new CommandInboxNotConfiguredException(commandType);
        }

        return _commandInbox;
    }

    private void ThrowIfResultCommandIsInboxed(Type commandType)
    {
        if (HasStoreInInboxAttribute(commandType) && IsResultCommandType(commandType))
        {
            throw new ResultCommandInboxNotSupportedException(commandType);
        }
    }

    private static bool IsInboxExecution(CommandMediationSettings? settings)
    {
        return settings?.Items.TryGetValue(CommandInboxExecutionContextKeys.IsInboxExecution, out var value) == true &&
               value is true;
    }

    private static bool IsResultCommandType(Type commandType)
    {
        foreach (var interfaceType in commandType.GetInterfaces())
        {
            if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(ICommand<>))
            {
                return true;
            }
        }

        return false;
    }
}