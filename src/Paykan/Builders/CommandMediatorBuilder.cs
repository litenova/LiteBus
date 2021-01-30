using System;
using Paykan.Abstractions;
using Paykan.Internal;
using Paykan.Internal.Mediators;
using Paykan.Registry.Abstractions;

namespace Paykan.Builders
{
    public class CommandMediatorBuilder
    {
        public ICommandMediator Build(IServiceProvider serviceProvider,
                                      IMessageRegistry messageRegistry)
        {
            return new CommandMediator(serviceProvider, messageRegistry);
        }
    }
}