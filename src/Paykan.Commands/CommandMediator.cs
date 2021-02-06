using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Paykan.Commands.Abstraction;
using Paykan.Messaging.Abstractions.Extensions;
using Paykan.Registry.Abstractions;

namespace Paykan.Commands
{
    /// <inheritdoc cref="ICommandMediator" />
    internal class CommandMediator : ICommandMediator
    {
        private readonly IMessageRegistry _messageRegistry;
        private readonly IServiceProvider _serviceProvider;

        public CommandMediator(IServiceProvider serviceProvider,
                               IMessageRegistry messageRegistry)
        {
            _serviceProvider = serviceProvider;
            _messageRegistry = messageRegistry;
        }

        public Task SendAsync<TCommand>(TCommand command, CancellationToken cancellationToken = default)
            where TCommand : ICommand
        {
            var commandType = typeof(TCommand);

            var descriptor = _messageRegistry.GetDescriptor<TCommand>();

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleCommandHandlerFoundException(commandType);

            var handler = _serviceProvider.GetHandler<TCommand, Task>(descriptor.HandlerTypes.Single());

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

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleCommandHandlerFoundException(commandType);

            var handler =
                _serviceProvider.GetHandler<TCommand, Task<TCommandResult>>(descriptor.HandlerTypes.Single());

            return handler.HandleAsync(command, cancellationToken);
        }

        public Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                              CancellationToken cancellationToken = default)
        {
            var commandType = command.GetType();

            var descriptor = _messageRegistry.GetDescriptor(command.GetType());

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleCommandHandlerFoundException(commandType);

            return _serviceProvider
                   .GetService(descriptor.HandlerTypes.First())
                   .HandleAsync<Task<TCommandResult>>(command, cancellationToken);
        }
    }
}