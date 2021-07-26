using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Exceptions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Commands
{
    /// <inheritdoc cref="ICommandMediator" />
    public class CommandMediator : ICommandMediator
    {
        private readonly IMessageRegistry _messageRegistry;
        private readonly IServiceProvider _serviceProvider;

        public CommandMediator(IServiceProvider serviceProvider,
                               IMessageRegistry messageRegistry)
        {
            _serviceProvider = serviceProvider;
            _messageRegistry = messageRegistry;
        }

        private HandlerInstanceDescriptor GetHandlers(object command)
        {
            var commandType = command.GetType();

            var descriptor = _messageRegistry.GetDescriptor(commandType);

            if (descriptor.HandlerTypes.Count > 1) throw new MultipleCommandHandlerFoundException(commandType);

            var handler = _serviceProvider.GetService(descriptor.HandlerTypes.Single()) as IAsyncMessageHandler;

            var preHandleHooks = _serviceProvider.GetPreHandleHooks(descriptor.PreHandleHookDescriptors);
            var postHandleHooks = _serviceProvider.GetPostHandleHooks(descriptor.PostHandleHookDescriptors);

            return new HandlerInstanceDescriptor
            {
                Handler = handler,
                PreHandlers = preHandleHooks,
                PostHandlers = postHandleHooks,
            };
        }

        public async Task SendAsync(ICommand command, CancellationToken cancellationToken = default)
        {
            var handlerDescriptor = GetHandlers(command);

            foreach (var preHandleHook in handlerDescriptor.PreHandlers)
            {
                await preHandleHook.ExecuteAsync(command, cancellationToken);
            }

            await handlerDescriptor.Handler.HandleAsync(command, cancellationToken);

            foreach (var postHandleHook in handlerDescriptor.PostHandlers)
            {
                await postHandleHook.ExecuteAsync(command, cancellationToken);
            }
        }

        public async Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                                    CancellationToken cancellationToken = default)
        {
            var handlerDescriptor = GetHandlers(command);

            foreach (var preHandleHook in handlerDescriptor.PreHandlers)
            {
                await preHandleHook.ExecuteAsync(command, cancellationToken);
            }

            TCommandResult commandResult =
                await (Task<TCommandResult>) handlerDescriptor.Handler.HandleAsync(command, cancellationToken);

            foreach (var postHandleHook in handlerDescriptor.PostHandlers)
            {
                await postHandleHook.ExecuteAsync(command, cancellationToken);
            }

            return commandResult;
        }
    }
}