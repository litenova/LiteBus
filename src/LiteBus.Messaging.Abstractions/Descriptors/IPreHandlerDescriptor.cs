using System;

namespace LiteBus.Messaging.Abstractions;

public interface IPreHandlerDescriptor : IDescriptor
{
    Type PreHandlerType { get; }

    int Order { get; }

    bool IsGeneric { get; }
}