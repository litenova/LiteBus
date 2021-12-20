using System;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry.Descriptors
{
    internal class PreHandlerDescriptor : IPreHandlerDescriptor
    {
        public PreHandlerDescriptor(Type preHandlerType, Type messageType, int order)
        {
            PreHandlerType = preHandlerType;
            Order = order;
            MessageType = messageType;
        }

        public Type PreHandlerType { get; }

        public int Order { get; }

        public Type MessageType { get; }
    }
}