using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paykan.Abstractions;
using Paykan.Abstractions.Interceptors;
using Paykan.Internal.Exceptions;
using Paykan.Internal.Extensions;

namespace Paykan.Internal.Mediators
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

        public Task SendAsync<TCommand>(TCommand command, 
                                        CancellationToken cancellationToken = default)
            where TCommand : ICommand
        {
            var commandType = typeof(TCommand);

            var descriptor = _messageRegistry[commandType];

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(commandType.Name);

            var handler = _serviceProvider.GetHandler<TCommand, Task>(descriptor.HandlerTypes.First());

            var postHandleHooks = _serviceProvider.GetPostHandleHooks<TCommand>(descriptor.PostHandleHookTypes);

            return handler
                   .HandleAsync(command, cancellationToken)
                   .ContinueWith(t => Task.WhenAll(postHandleHooks.Select(h => h.ExecuteAsync(command))),
                                 cancellationToken);
        }

        public Task<TCommandResult> SendAsync<TCommand, TCommandResult>(TCommand command,
                                                                        CancellationToken cancellationToken =
                                                                            default)
            where TCommand : ICommand<TCommandResult>
        {
            var commandType = typeof(TCommand);

            var descriptor = _messageRegistry[commandType];

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(commandType.Name);

            var handler = _serviceProvider
                .GetHandler<TCommand, Task<TCommandResult>>(descriptor.HandlerTypes.First());

            return handler.HandleAsync(command, cancellationToken);
        }

        public Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command, 
                                                              CancellationToken cancellationToken = default)
        {
            var commandType = command.GetType();
            
            var descriptor = _messageRegistry[commandType];

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(commandType.Name);
            
            return _serviceProvider
                   .GetService(descriptor.HandlerTypes.First())
                   .HandleAsync<Task<TCommandResult>>(command, cancellationToken);
        }
    }
}