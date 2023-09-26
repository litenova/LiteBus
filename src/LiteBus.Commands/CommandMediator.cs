using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Commands;

/// <inheritdoc cref="ICommandMediator" />
public class CommandMediator : ICommandMediator
{
    private readonly IMessageMediator _messageMediator;

    public CommandMediator(IMessageMediator messageMediator)
    {
        _messageMediator = messageMediator;
    }

    public Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        var mediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand>();

        var findStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        var options = new MediateOptions<ICommand, Task>
        {
            MessageMediationStrategy = mediationStrategy,
            MessageResolveStrategy = findStrategy,
            CancellationToken = cancellationToken
        };

        return _messageMediator.Mediate(command, options);
    }

    public Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                          CancellationToken cancellationToken = default)
    {
        var mediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand<TCommandResult>, TCommandResult>();
        var findStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        var options = new MediateOptions<ICommand<TCommandResult>, Task<TCommandResult>>
        {
            MessageResolveStrategy = findStrategy,
            MessageMediationStrategy = mediationStrategy,
            CancellationToken = cancellationToken
        };

        return _messageMediator.Mediate(command, options);
    }
}