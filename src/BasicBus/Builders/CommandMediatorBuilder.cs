using System;
using BasicBus.Abstractions;
using BasicBus.Internal;

namespace BasicBus.Builders
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