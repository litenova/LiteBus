using System;
using LiteBus.Messaging.Abstractions.Metadata;

namespace LiteBus.Messaging.Internal.Registry.Descriptors;

internal class PreHandlerDescriptor : HandlerDescriptor, IPreHandlerDescriptor
{
    public PreHandlerDescriptor(Type handlerType, Type messageType, Type outputType, int order) :
        base(handlerType, messageType, null, outputType, order)
    {
    }
}