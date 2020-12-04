using System;
using System.Collections.Generic;
using System.Reflection;

namespace BasicBus.Abstractions
{
    public interface ICommandHandlerRegistry
    {
        IReadOnlyCollection<ICommandDescriptor> CommandDescriptors { get; }
        IReadOnlyCollection<Type> GlobalInterceptors { get; }
        ICommandDescriptor this[Type commandType] { get; }
    }
}