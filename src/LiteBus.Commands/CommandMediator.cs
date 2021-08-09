using System;
using System.Threading;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.FindStrategies;
using LiteBus.Messaging.Abstractions.MediationStrategies;
using MorseCode.ITask;

namespace LiteBus.Commands
{
    /// <inheritdoc cref="ICommandMediator" />
    public class CommandMediator : ICommandMediator
    {
        private readonly IMessageMediator _messageMediator;

        public CommandMediator(IMessageMediator messageMediator)
        {
            _messageMediator = messageMediator;
        }

        public async ITask SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default) where TCommand : ICommand
        {
            var mediationStrategy = new SingleAsyncHandlerMessageMediationStrategy<TCommand>(cancellationToken);

            var findStrategy = new ActualTypeOrBaseTypeMessageResolveStrategy<TCommand>();

            await _messageMediator.Mediate(command, findStrategy, mediationStrategy);
        }

        public async ITask<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                                    CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}