using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicBus.Abstractions;
using BasicBus.Internal.Wrappers;

namespace BasicBus
{
    public class CommandMediator : ICommandMediator
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ICommandHandlerRegistry _commandHandlerRegistry;

        public CommandMediator(IServiceProvider serviceProvider, ICommandHandlerRegistry commandHandlerRegistry)
        {
            _serviceProvider = serviceProvider;
            _commandHandlerRegistry = commandHandlerRegistry;
        }

        public async Task SendAsync(ICommand command,
                                    CancellationToken cancellationToken = default)
        {
            var commandType = command.GetType();

            var commandDescriptor = _commandHandlerRegistry[command.GetType()];

            var handlerInstance = _serviceProvider.GetService(commandDescriptor.Handler);

            var interceptors = commandDescriptor.Interceptors
                                                .Select(i => _serviceProvider.GetService(i));

            var globalInterceptors = _commandHandlerRegistry.GlobalInterceptors
                .Select(gi => _serviceProvider.GetService(gi));

            var handlerWrapper =
                CommandHandlerWrapper.Create(commandType, handlerInstance, interceptors, globalInterceptors);

            var result = await handlerWrapper.HandleAsync(command, cancellationToken);
        }

        public async Task<TCommandResult> SendAsync<TCommandResult>(ICommand<TCommandResult> command,
                                                                    CancellationToken cancellationToken = default)
        {
            var commandType = command.GetType();

            var commandDescriptor = _commandHandlerRegistry[command.GetType()];

            var handlerInstance = _serviceProvider.GetService(commandDescriptor.Handler);

            var interceptors = commandDescriptor.Interceptors
                                                .Select(i => _serviceProvider.GetService(i));

            var globalInterceptors = _commandHandlerRegistry.GlobalInterceptors
                .Select(gi => _serviceProvider.GetService(gi));

            var handlerWrapper =
                CommandHandlerWrapper.Create(commandType, handlerInstance, interceptors, globalInterceptors);

            var result = await handlerWrapper.HandleAsync(command, cancellationToken);

            return await (Task<TCommandResult>) result;
        }

        public void Send(ICommand command)
        {
            throw new System.NotImplementedException();
        }

        public TCommandResult Send<TCommandResult>(ICommand<TCommandResult> command)
        {
            throw new System.NotImplementedException();
        }
    }
}