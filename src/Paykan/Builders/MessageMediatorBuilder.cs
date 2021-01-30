using System;
using Paykan.Abstractions;
using Paykan.Internal.Mediators;
using Paykan.Messaging.Abstractions;
using Paykan.Registry.Abstractions;

namespace Paykan.Builders
{
    public class MessageMediatorBuilder
    {
        public IMessageMediator Build(IServiceProvider serviceProvider,
                                      IMessageRegistry messageRegistry)
        {
            return new MessageMediator(serviceProvider, messageRegistry);
        }
    }
}