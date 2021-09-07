using System.Reflection;
using LiteBus.Events.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Extensions;

namespace LiteBus.Events.Extensions.MicrosoftDependencyInjection
{
    public class LiteBusEventBuilder
    {
        private readonly IMessageRegistry _messageRegistry;
        
        public LiteBusEventBuilder(IMessageRegistry messageRegistry)
        {
            _messageRegistry = messageRegistry;
        }

        public LiteBusEventBuilder RegisterFrom(Assembly assembly)
        {
            _messageRegistry.Register(assembly);
            return this;
        }

        public LiteBusEventBuilder RegisterHandler<THandler>() where THandler : IEventHandlerBase
        {
            _messageRegistry.RegisterHandler(typeof(THandler));

            return this;
        }

        public LiteBusEventBuilder RegisterPreHandler<TEventPreHandler>()
            where TEventPreHandler : IEventPreHandlerBase
        {
            _messageRegistry.RegisterPreHandler(typeof(TEventPreHandler));

            return this;
        }
        
        public LiteBusEventBuilder RegisterPostHandler<TEventPostHandler>() 
            where TEventPostHandler : IEventPostHandlerBase
        {
            _messageRegistry.RegisterPostHandler(typeof(TEventPostHandler));

            return this;
        }
    }
}