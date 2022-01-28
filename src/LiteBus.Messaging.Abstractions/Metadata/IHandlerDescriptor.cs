using System;

namespace LiteBus.Messaging.Abstractions.Metadata;

public interface IHandlerDescriptor : IDescriptor
{
    Type HandlerType { get; }

    Type MessageResultType { get; }

    bool IsGeneric { get; }

    int Order { get; }
}