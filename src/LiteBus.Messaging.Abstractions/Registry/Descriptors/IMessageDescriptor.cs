using System;
using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions;

public interface IMessageDescriptor
{
    Type MessageType { get; }

    bool IsGeneric { get; }

    IReadOnlyCollection<IMainHandlerDescriptor> Handlers { get; }

    IReadOnlyCollection<IMainHandlerDescriptor> IndirectHandlers { get; }

    IReadOnlyCollection<IPostHandlerDescriptor> PostHandlers { get; }

    IReadOnlyCollection<IPostHandlerDescriptor> IndirectPostHandlers { get; }

    IReadOnlyCollection<IPreHandlerDescriptor> PreHandlers { get; }

    IReadOnlyCollection<IPreHandlerDescriptor> IndirectPreHandlers { get; }

    IReadOnlyCollection<IErrorHandlerDescriptor> ErrorHandlers { get; }

    IReadOnlyCollection<IErrorHandlerDescriptor> IndirectErrorHandlers { get; }
}