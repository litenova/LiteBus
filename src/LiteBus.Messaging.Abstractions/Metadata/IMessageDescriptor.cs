using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.Metadata;

public interface IMessageDescriptor : IDescriptor
{
    bool IsGeneric { get; }

    IReadOnlyCollection<IHandlerDescriptor> Handlers { get; }

    IReadOnlyCollection<IHandlerDescriptor> IndirectHandlers { get; }

    IReadOnlyCollection<IPostHandlerDescriptor> PostHandlers { get; }

    IReadOnlyCollection<IPostHandlerDescriptor> IndirectPostHandlers { get; }

    IReadOnlyCollection<IPreHandlerDescriptor> PreHandlers { get; }

    IReadOnlyCollection<IPreHandlerDescriptor> IndirectPreHandlers { get; }

    IReadOnlyCollection<IErrorHandlerDescriptor> ErrorHandlers { get; }

    IReadOnlyCollection<IErrorHandlerDescriptor> IndirectErrorHandlers { get; }
}