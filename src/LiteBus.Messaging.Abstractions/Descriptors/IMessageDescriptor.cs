using System.Collections.Generic;

namespace LiteBus.Messaging.Abstractions.Descriptors;

public interface IMessageDescriptor : IDescriptor
{
    IReadOnlyCollection<IHandlerDescriptor> HandlerDescriptors { get; }

    IReadOnlyCollection<IPostHandlerDescriptor> PostHandlerDescriptors { get; }

    IReadOnlyCollection<IPreHandlerDescriptor> PreHandlerDescriptors { get; }

    bool IsGeneric { get; }

    IReadOnlyCollection<IErrorHandlerDescriptor> ErrorHandlerDescriptors { get; }
}