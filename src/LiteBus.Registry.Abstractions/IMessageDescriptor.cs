using System;
using System.Collections.Generic;

namespace LiteBus.Registry.Abstractions
{
    public interface IMessageDescriptor
    {
        Type MessageType { get; }

        IReadOnlyCollection<Type> HandlerTypes { get; }

        IReadOnlyCollection<Type> PostHandleHookTypes { get; }
        
        IReadOnlyCollection<Type> PreHandleHookTypes { get; }
    }
}