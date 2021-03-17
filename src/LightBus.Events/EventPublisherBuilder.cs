using System;
using LightBus.Events.Abstractions;
using LightBus.Registry.Abstractions;

namespace LightBus.Events
{
    public class EventPublisherBuilder
    {
        public IEventPublisher Build(IServiceProvider serviceProvider,
                                     IMessageRegistry messageRegistry)
        {
            return new EventMediator(serviceProvider, messageRegistry);
        }
    }
}