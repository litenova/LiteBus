using System;
using System.Collections.Generic;

namespace BasicBus.Abstractions
{
    public interface ICommandDescriptor
    {
        Type Command { get; }
        Type Handler { get; }
        IReadOnlyCollection<Type> Interceptors { get; }
    }
}