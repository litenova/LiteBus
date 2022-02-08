using System;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Internal.Registry.Descriptors;

internal class PostHandlerDescriptor : HandlerDescriptor, IPostHandlerDescriptor
{
    public PostHandlerDescriptor(Type handlerType,
                                 Type messageType,
                                 Type messageResultType,
                                 Type outputType,
                                 int order) :
        base(handlerType, messageType, messageResultType, outputType, order)
    {
    }
}