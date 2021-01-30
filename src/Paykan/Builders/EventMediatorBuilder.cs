using System;
using Paykan.Abstractions;
using Paykan.Internal;
using Paykan.Internal.Mediators;
using Paykan.Registry.Abstractions;

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