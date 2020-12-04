using System;
using Paykan.Abstractions;
using Paykan.Internal;

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