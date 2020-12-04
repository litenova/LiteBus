using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BasicBus.Abstractions;

namespace BasicBus.Internal.Wrappers
{
    internal abstract class CommandHandlerWrapper
    {
        public abstract Task<object> HandleAsync(ICommand command,
                                                 CancellationToken cancellation = default);

        public static CommandHandlerWrapper Create(Type commandType,
                                                   Type commandResultType,
                                                   object handler,
                                                   ICollection<object> interceptors,
                                                   ICollection<object> globalInterceptors)
        {
            var genericType = typeof(CommandHandlerWrapper<,>).MakeGenericType(commandType, commandResultType);

            var instance = Activator.CreateInstance(genericType, handler, interceptors, globalInterceptors);

            return (CommandHandlerWrapper) instance;
        }

        public static CommandHandlerWrapper Create(Type commandType,
                                                   object handler,
                                                   IEnumerable<object> interceptors,
                                                   IEnumerable<object> globalInterceptors)
        {
            var genericType = typeof(CommandHandlerWrapper<>).MakeGenericType(commandType);

            var instance = Activator.CreateInstance(genericType,
                                                    handler,
                                                    interceptors,
                                                    globalInterceptors);

            return (CommandHandlerWrapper) instance;
        }
    }

    internal class CommandHandlerWrapper<TCommand> : CommandHandlerWrapper where TCommand : ICommand
    {
        private readonly ICommandHandler<TCommand> _handler;
        private readonly List<ICommandInterceptor<TCommand>> _interceptors;
        private readonly List<ICommandInterceptor> _globalInterceptors;

        public CommandHandlerWrapper(object handler,
                                     IEnumerable<object> interceptors,
                                     IEnumerable<object> globalInterceptors)
        {
            _globalInterceptors = globalInterceptors.Cast<ICommandInterceptor>().ToList();
            _interceptors = interceptors.Cast<ICommandInterceptor<TCommand>>().ToList();
            _handler = (ICommandHandler<TCommand>) handler;
        }

        public override async Task<object> HandleAsync(ICommand command, CancellationToken cancellation = default)
        {
            await Task
                .WhenAll(_globalInterceptors.Select(i => i.OnPreHandleAsync(command)));

            await Task
                .WhenAll(_interceptors.Select(i => i.OnPreHandleAsync((TCommand) command)));

            await _handler.HandleAsync((TCommand) command, cancellation);

            await Task
                .WhenAll(_globalInterceptors.Select(i => i.OnPostHandleAsync(command)));

            await Task
                .WhenAll(_interceptors.Select(i => i.OnPostHandleAsync((TCommand) command)));

            return null;
        }
    }

    internal class CommandHandlerWrapper<TCommand, TCommandResult> : CommandHandlerWrapper
        where TCommand : ICommand<TCommandResult>
    {
        private readonly ICommandHandler<TCommand, TCommandResult> _handler;
        private readonly List<ICommandInterceptor<TCommand, TCommandResult>> _interceptors;
        private readonly List<ICommandInterceptor> _globalInterceptors;

        public CommandHandlerWrapper(object handler,
                                     IEnumerable<object> interceptors,
                                     IEnumerable<object> globalInterceptors)
        {
            _globalInterceptors = globalInterceptors.Cast<ICommandInterceptor>().ToList();
            _interceptors = interceptors.Cast<ICommandInterceptor<TCommand, TCommandResult>>().ToList();
            _handler = (ICommandHandler<TCommand, TCommandResult>) handler;
        }

        public override async Task<object> HandleAsync(ICommand command, CancellationToken cancellation = default)
        {
            await Task
                .WhenAll(_globalInterceptors.Select(i => i.OnPreHandleAsync(command)));

            await Task
                .WhenAll(_interceptors.Select(i => i.OnPreHandleAsync((TCommand) command)));

            await _handler.HandleAsync((TCommand) command, cancellation);

            await Task
                .WhenAll(_globalInterceptors.Select(i => i.OnPostHandleAsync(command)));

            await Task
                .WhenAll(_interceptors.Select(i => i.OnPostHandleAsync((TCommand) command)));

            return null;
        }
    }
}