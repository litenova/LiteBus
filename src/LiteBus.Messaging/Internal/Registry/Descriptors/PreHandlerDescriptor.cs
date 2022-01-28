using System;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Internal.Registry.Descriptors;

internal class PreHandlerDescriptor : IPreHandlerDescriptor
{
    public PreHandlerDescriptor(Type preHandlerType, Type messageType, int order)
    {
        PreHandlerType = preHandlerType;
        Order = order;
        IsGeneric = messageType.IsGenericType;
        MessageType = IsGeneric ? messageType.GetGenericTypeDefinition() : messageType;
    }

    public Type PreHandlerType { get; }

    public int Order { get; }

    public bool IsGeneric { get; }

    public Type MessageType { get; }
}