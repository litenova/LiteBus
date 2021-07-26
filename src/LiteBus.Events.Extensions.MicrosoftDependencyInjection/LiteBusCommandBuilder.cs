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

        public LiteBusEventBuilder Register(Assembly assembly)
        {
            _messageRegistry.Register(assembly);
            return this;
        }


        public LiteBusEventBuilder RegisterHandler<THandler, TEvent>()
            where THandler : IEventHandler<TEvent>
            where TEvent : IEvent
        {
            _messageRegistry.RegisterHandler(typeof(THandler));

            return this;
        }
    }
}