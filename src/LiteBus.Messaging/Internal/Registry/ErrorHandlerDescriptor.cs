using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class ErrorHandlerDescriptor : IErrorHandlerDescriptor
    {
        public ErrorHandlerDescriptor(Type errorHandlerType, Type messageType)
        {
            ErrorHandlerType = errorHandlerType;
            MessageType = messageType;

            var handlerOrderAttribute =
                (HandlerOrderAttribute)Attribute.GetCustomAttribute(errorHandlerType, typeof(HandlerOrderAttribute));

            if (handlerOrderAttribute is not null)
            {
                Order = handlerOrderAttribute.Order;
            }
        }

        public Type ErrorHandlerType { get; }

        public int Order { get; }

        public Type MessageType { get; }
    }
}