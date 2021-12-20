using System;

namespace LiteBus.Messaging.Abstractions.Descriptors;

public interface IHandlerDescriptor : IDescriptor
{
    Type HandlerType { get; }

    Type MessageResultType { get; }

    bool IsGeneric { get; }
}