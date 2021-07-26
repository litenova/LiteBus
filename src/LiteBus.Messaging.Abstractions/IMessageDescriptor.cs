using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions
{
    public interface IMessageDescriptor
    {
        Type MessageType { get; }

        IReadOnlyCollection<Type> HandlerTypes { get; }

        IReadOnlyCollection<IHookDescriptor> PostHandleHookDescriptors { get; }

        IReadOnlyCollection<IHookDescriptor> PreHandleHookDescriptors { get; }
    }
}