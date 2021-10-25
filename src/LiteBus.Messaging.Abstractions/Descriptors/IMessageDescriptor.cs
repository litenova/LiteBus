using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.Descriptors
{
    public interface IMessageDescriptor
    {
        Type MessageType { get; }

        IReadOnlyCollection<IHandlerDescriptor> Handlers { get; }

        IReadOnlyCollection<IPostHandlerDescriptor> PostHandlers { get; }

        IReadOnlyCollection<IPreHandlerDescriptor> PreHandlers { get; }

        bool IsGeneric { get; }
        IReadOnlyCollection<IErrorHandlerDescriptor> ErrorHandlers { get; }
    }
}