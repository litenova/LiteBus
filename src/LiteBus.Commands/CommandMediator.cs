using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.FindStrategies;
using LiteBus.Messaging.Abstractions.MediationStrategies;

namespace LiteBus.Commands;

/// <inheritdoc cref="ICommandMediator" />
public class CommandMediator : ICommandMediator
{
    private readonly IMessageMediator _messageMediator;

    public CommandMediator(IMessageMediator messageMediator)
    {
        _messageMediator = messageMediator;
    }

    public async Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
    {
        var mediationStrategy = new SingleAsyncHandlerMediationStrategy<ICommand>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        await _messageMediator.Mediate(command, findStrategy, mediationStrategy);
    }

    public async Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                                CancellationToken cancellationToken = default)
    {
        var mediationStrategy =
            new SingleAsyncHandlerMediationStrategy<ICommand<TCommandResult>, TCommandResult>(cancellationToken);

        var findStrategy = new ActualTypeOrFirstAssignableTypeMessageResolveStrategy();

        return await _messageMediator.Mediate(command, findStrategy, mediationStrategy);
    }
}