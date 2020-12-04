using System;
using System.Threading;
using System.Threading.Tasks;
using Paykan.Abstractions;
using Paykan.Internal.Extensions;

namespace Paykan.Internal
{
    internal class CommandMediator : ICommandMediator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMessageRegistry _messageRegistry;

        public CommandMediator(IServiceProvider serviceProvider,
                               IMessageRegistry messageRegistry)
        {
            _serviceProvider = serviceProvider;
            _messageRegistry = messageRegistry;
        }

        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand
        {
            var descriptor = _messageRegistry[command.GetType()];

            var handler = _serviceProvider.GetHandler<TCommand, Task>(descriptor.HandlerType);

            return handler.HandleAsync(command, cancellationToken);
        }

        public Task<TCommandResult> SendAsync<TCommand, TCommandResult>(TCommand command,
            CancellationToken cancellationToken = default) where TCommand : ICommand<TCommandResult>
        {
            var descriptor = _messageRegistry[command.GetType()];

            var handler = _serviceProvider.GetHandler<TCommand, Task<TCommandResult>>(descriptor.HandlerType);

            return handler.HandleAsync(command, cancellationToken);
        }
    }
}