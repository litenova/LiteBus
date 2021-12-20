using System;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry.Descriptors;

internal class PostHandlerDescriptor : IPostHandlerDescriptor
{
    public PostHandlerDescriptor(Type postHandlerType, Type messageType, Type? messageResultType, int order)
    {
        PostHandlerType = postHandlerType;
        MessageType = messageType;
        MessageResultType = messageResultType;
        Order = order;
    }

    public Type PostHandlerType { get; }

    public Type MessageType { get; }

    public Type? MessageResultType { get; }

    public int Order { get; }
}