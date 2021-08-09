using System.Reflection;
using LiteBus.Commands.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Commands.Extensions.MicrosoftDependencyInjection
{
    public class LiteBusCommandBuilder
    {
        private readonly IMessageRegistry _messageRegistry;

        public LiteBusCommandBuilder(IMessageRegistry messageRegistry)
        {
            _messageRegistry = messageRegistry;
        }

        public LiteBusCommandBuilder Register(Assembly assembly)
        {
            _messageRegistry.Register(assembly);
            return this;
        }

        public LiteBusCommandBuilder RegisterHandler<THandler, TCommand, TCommandResult>()
            where THandler : ICommandHandler<TCommand, TCommandResult>
            where TCommand : ICommand<TCommandResult>
        {
            _messageRegistry.RegisterHandler(typeof(THandler));

            return this;
        }

        public LiteBusCommandBuilder RegisterHandler<THandler, TCommand>()
            where THandler : ICommandHandler<TCommand>
            where TCommand : ICommand
        {
            _messageRegistry.RegisterHandler(typeof(THandler));

            return this;
        }

        public LiteBusCommandBuilder RegisterPreHandleHook<THook, TCommand>()
            where THook : ICommandPreHandleAsyncHook<TCommand>
            where TCommand : ICommand
        {
            _messageRegistry.RegisterPreHandleHook(typeof(THook));

            return this;
        }

        public LiteBusCommandBuilder RegisterPreHandleHook<THook>()
            where THook : ICommandPreHandleAsyncHook
        {
            _messageRegistry.RegisterPreHandleHook(typeof(THook));

            return this;
        }

        public LiteBusCommandBuilder RegisterPostHandleHook<THook, TCommand>()
            where THook : ICommandPostHandleAsyncHook<TCommand>
            where TCommand : ICommand
        {
            _messageRegistry.RegisterPostHandleHook(typeof(THook));

            return this;
        }

        public LiteBusCommandBuilder RegisterPostHandleHook<THook>()
            where THook : ICommandPostHandleAsyncHook
        {
            _messageRegistry.RegisterPostHandleHook(typeof(THook));

            return this;
        }
    }
}