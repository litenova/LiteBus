using System;
using Paykan.Commands.Abstraction;
using Paykan.Registry.Abstractions;

namespace Paykan.Commands
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