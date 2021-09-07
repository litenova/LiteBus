using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class PreHandlerDescriptor : IPreHandlerDescriptor
    {
        public PreHandlerDescriptor(Type hookType, Type messageType)
        {
            PreHandlerType = hookType;
            MessageType = messageType;

            var handlerOrderAttribute =
                (HandlerOrderAttribute)Attribute.GetCustomAttribute(hookType, typeof(HandlerOrderAttribute));

            if (handlerOrderAttribute is not null)
            {
                Order = handlerOrderAttribute.Order;
            }
        }

        public Type PreHandlerType { get; }

        public int Order { get; }

        public Type MessageType { get; }
    }
}