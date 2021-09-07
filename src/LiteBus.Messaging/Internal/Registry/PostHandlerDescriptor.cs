using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class PostHandlerDescriptor : IPostHandlerDescriptor
    {
        public PostHandlerDescriptor(Type hookType, Type messageType, Type messageResultType)
        {
            PostHandlerType = hookType;
            MessageType = messageType;
            MessageResultType = messageResultType;

            var hookOrderAttribute =
                (HandlerOrderAttribute)Attribute.GetCustomAttribute(hookType, typeof(HandlerOrderAttribute));

            if (hookOrderAttribute is not null)
            {
                Order = hookOrderAttribute.Order;
            }
        }

        public Type PostHandlerType { get; }

        public int Order { get; }

        public Type MessageType { get; }

        public Type MessageResultType { get; }
    }
}