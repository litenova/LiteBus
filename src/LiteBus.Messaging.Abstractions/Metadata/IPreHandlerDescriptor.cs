using System;

namespace LiteBus.Messaging.Abstractions.Metadata;

public interface IPreHandlerDescriptor : IDescriptor
{
    Type PreHandlerType { get; }

    int Order { get; }

    bool IsGeneric { get; }
}