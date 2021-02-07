using System;
using Paykan.Events.Abstractions;
using Paykan.Registry.Abstractions;

namespace Paykan.Events
{
    public class EventMediatorBuilder
    {
        public IEventMediator Build(IServiceProvider serviceProvider,
                                    IMessageRegistry messageRegistry)
        {
            return new EventMediator(serviceProvider, messageRegistry);
        }
    }
}