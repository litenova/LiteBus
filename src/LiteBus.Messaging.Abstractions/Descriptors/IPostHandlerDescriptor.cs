using System;

namespace LiteBus.Messaging.Abstractions.Descriptors;

public interface IPostHandlerDescriptor : IDescriptor
{
    Type PostHandlerType { get; }

    int Order { get; }

    Type MessageResultType { get; }

    bool IsGeneric { get; }
}