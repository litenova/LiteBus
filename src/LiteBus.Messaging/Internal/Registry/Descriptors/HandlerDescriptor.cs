using System;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry.Descriptors
{
    internal class HandlerDescriptor : IHandlerDescriptor
    {
        public HandlerDescriptor(Type handlerType, Type messageType, Type messageResultType, int order, bool isGeneric)
        {
            HandlerType = handlerType;
            MessageType = messageType;
            MessageResultType = messageResultType;
            IsGeneric = isGeneric;
            Order = order;
        }

        public Type HandlerType { get; }

        public Type MessageType { get; }

        public Type MessageResultType { get; }

        public bool IsGeneric { get; }

        public int Order { get; }
    }
}