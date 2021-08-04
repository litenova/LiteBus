using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.Descriptors
{
    public interface IMessageDescriptor
    {
        IMessageDescriptor Base { get; }
        Type MessageType { get; }

        IReadOnlyCollection<IHandlerDescriptor> HandlerDescriptors { get; }

        IReadOnlyCollection<IHookDescriptor> PostHandleHookDescriptors { get; }

        IReadOnlyCollection<IHookDescriptor> PreHandleHookDescriptors { get; }
    }
}