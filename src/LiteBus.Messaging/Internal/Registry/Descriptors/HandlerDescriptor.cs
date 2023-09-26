using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Registry.Descriptors;

internal class HandlerDescriptor : IHandlerDescriptor
{
    public HandlerDescriptor(Type handlerType, Type messageType, Type messageResultType, int order)
    {
        HandlerType = handlerType;
        IsGeneric = messageType.IsGenericType;
        MessageType = IsGeneric ? messageType.GetGenericTypeDefinition() : messageType;
        MessageResultType = messageResultType;
        Order = order;
    }

    public int Order { get; }

    public Type HandlerType { get; }

    public Type MessageType { get; }

    public Type MessageResultType { get; }

    public bool IsGeneric { get; }
}