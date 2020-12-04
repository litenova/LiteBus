using System;
using Paykan.Abstractions;
using Paykan.Internal;

namespace Paykan.Builders
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