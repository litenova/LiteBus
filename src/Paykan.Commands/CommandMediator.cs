using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paykan.Commands.Abstraction;
using Paykan.Messaging.Abstractions;
using Paykan.Registry.Abstractions;

namespace Paykan.Commands
{
    internal class CommandMediator : ICommandMediator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMessageRegistry _messageRegistry;

        private readonly IMessageMediator _messageMediator;

        public CommandMediator(IServiceProvider serviceProvider,
                               IMessageRegistry messageRegistry)
        {
            _serviceProvider = serviceProvider;
            _messageRegistry = messageRegistry;
            
            _messageMediator.Send<object, Task>(null, config =>
            {
                c
            })
        }

        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand
        {
            var commandType = typeof(TCommand);

            var descriptor = _messageRegistry.GetDescriptor<TCommand>();

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(commandType.Name);

            var handler =
                _serviceProvider.GetHandler<TCommand, Task>(Enumerable.First(descriptor.HandlerTypes));

            var postHandleHooks = _serviceProvider.GetPostHandleHooks<TCommand>(descriptor.PostHandleHookTypes);

            return handler
                   .HandleAsync(command, cancellationToken)
                   .ContinueWith(t => Task.WhenAll((IEnumerable<Task>) postHandleHooks.Select(h => h.ExecuteAsync(command))),
                                 cancellationToken);
        }

        public Task<TCommandResult> SendAsync<TCommand, TCommandResult>(TCommand command,
                                                                        CancellationToken cancellationToken =
                                                                            default)
            where TCommand : ICommand<TCommandResult>
        {
            var commandType = typeof(TCommand);

            var descriptor = _messageRegistry.GetDescriptor<TCommand>();

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(commandType.Name);

            var handler = _serviceProvider
                .GetHandler<TCommand, Task<TCommandResult>>(Enumerable.First(descriptor.HandlerTypes));

            return handler.HandleAsync(command, cancellationToken);
        }

        public Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                              CancellationToken cancellationToken = default)
        {
            var commandType = command.GetType();

            var descriptor = _messageRegistry.GetDescriptor(command.GetType());

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleHandlerFoundException(commandType.Name);

            return _serviceProvider
                   .GetService(Enumerable.First(descriptor.HandlerTypes))
                   .HandleAsync<Task<TCommandResult>>(command, cancellationToken);
        }
    }
}