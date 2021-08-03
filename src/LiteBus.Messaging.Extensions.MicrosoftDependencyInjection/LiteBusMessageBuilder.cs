using System.Reflection;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Messaging.Extensions.MicrosoftDependencyInjection
{
    public class LiteBusMessageBuilder
    {
        private readonly IMessageRegistry _messageRegistry;

        public LiteBusMessageBuilder(IMessageRegistry messageRegistry)
        {
            _messageRegistry = messageRegistry;
        }

        public LiteBusMessageBuilder Register(Assembly assembly)
        {
            _messageRegistry.Register(assembly);
            return this;
        }

        public LiteBusMessageBuilder RegisterHandler<THandler, TMessage, TMessageResult>()
            where THandler : IMessageHandler<TMessage, TMessageResult>
        {
            _messageRegistry.RegisterHandler(typeof(THandler));

            return this;
        }

        public LiteBusMessageBuilder RegisterPreHandleHook<THook, TMessage>() where THook : IPreHandleHook<TMessage>
        {
            _messageRegistry.RegisterPreHandleHook(typeof(THook));

            return this;
        }

        public LiteBusMessageBuilder RegisterPostHandleHook<THook, TMessage>() where THook : IPostHandleHook<TMessage>
        {
            _messageRegistry.RegisterPostHandleHook(typeof(THook));

            return this;
        }
    }
}