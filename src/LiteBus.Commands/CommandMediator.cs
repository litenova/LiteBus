using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands;

/// <inheritdoc cref="ICommandMediator" />
public sealed class CommandMediator : ICommandMediator
{
    private readonly IMessageMediator _messageMediator;

    public CommandMediator(IMessageMediator messageMediator)
    {
        _messageMediator = messageMediator;
    }

    public Task SendAsync(ICommand command,
                          CommandMediationSettings? commandMediationSettings = null,
                          CancellationToken cancellationToken = default)
    {
        commandMediationSettings ??= new CommandMediationSettings();
        var mediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand>();

        var findStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        var options = new MediateOptions<ICommand, Task>
        {
            MessageMediationStrategy = mediationStrategy,
            MessageResolveStrategy = findStrategy,
            CancellationToken = cancellationToken,
            Tags = commandMediationSettings.Filters.Tags
        };

        return _messageMediator.Mediate(command, options);
    }

    public Task<TCommandResult?> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                           CommandMediationSettings? commandMediationSettings = null,
                                                           CancellationToken cancellationToken = default)
    {
        commandMediationSettings ??= new CommandMediationSettings();
        var mediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand<TCommandResult>, TCommandResult?>();
        var findStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        var options = new MediateOptions<ICommand<TCommandResult>, Task<TCommandResult?>>
        {
            MessageResolveStrategy = findStrategy,
            MessageMediationStrategy = mediationStrategy,
            CancellationToken = cancellationToken,
            Tags = commandMediationSettings.Filters.Tags
        };

        return _messageMediator.Mediate(command, options);
    }
}