using System;
using Paykan.Messaging.Abstractions;
using Paykan.Registry.Abstractions;

namespace Paykan.Messaging
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