using System;
using Paykan.Abstractions;
using Paykan.Internal.Mediators;

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