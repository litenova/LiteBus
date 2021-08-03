using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry
{
    internal class PostHandleHookDescriptor : IHookDescriptor
    {
        public PostHandleHookDescriptor(Type hookType, Type messageType)
        {
            HookType = hookType;
            MessageType = messageType;

            HookOrderAttribute hookOrderAttribute =
                (HookOrderAttribute) Attribute.GetCustomAttribute(hookType, typeof(HookOrderAttribute));

            if (hookOrderAttribute is not null)
            {
                Order = hookOrderAttribute.Order;
            }
        }

        public Type HookType { get; }

        public int Order { get; }

        public Type MessageType { get; }
    }
}