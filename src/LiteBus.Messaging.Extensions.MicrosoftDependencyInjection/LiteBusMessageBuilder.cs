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

        public LiteBusMessageBuilder RegisterFrom(Assembly assembly)
        {
            _messageRegistry.Register(assembly);
            return this;
        }

        public LiteBusMessageBuilder RegisterHandler<THandler>() where THandler : IMessageHandler
        {
            _messageRegistry.RegisterHandler(typeof(THandler));

            return this;
        }

        public LiteBusMessageBuilder RegisterPreHandler<TPreHandler>() where TPreHandler : IMessagePreHandler
        {
            _messageRegistry.RegisterPreHandler(typeof(TPreHandler));

            return this;
        }

        public LiteBusMessageBuilder RegisterPostHandler<TPostHandler>() where TPostHandler : IMessagePostHandler
        {
            _messageRegistry.RegisterPostHandler(typeof(TPostHandler));

            return this;
        }
    }
}