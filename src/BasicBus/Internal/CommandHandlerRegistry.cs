using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BasicBus.Abstractions;

namespace BasicBus.Internal
{
    internal class CommandHandlerRegistry : ICommandHandlerRegistry
    {
        private readonly IReadOnlyCollection<ICommandDescriptor> _commandDescriptors;
        private readonly IReadOnlyCollection<Type> _globalInterceptors;

        public CommandHandlerRegistry(IReadOnlyCollection<ICommandDescriptor> commandDescriptors,
                                      IReadOnlyCollection<Type> globalInterceptors)
        {
            _commandDescriptors = commandDescriptors;
            _globalInterceptors = globalInterceptors;
        }

        public IReadOnlyCollection<ICommandDescriptor> CommandDescriptors => _commandDescriptors;
        public IReadOnlyCollection<Type> GlobalInterceptors => _globalInterceptors;

        public ICommandDescriptor this[Type commandType] =>
            _commandDescriptors.Single(d => d.Command == commandType);
    }
}