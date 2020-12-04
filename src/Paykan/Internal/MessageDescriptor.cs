using System;
using Paykan.Abstractions;

namespace Paykan.Internal
{
    internal class MessageDescriptor : IMessageDescriptor
    {
        public MessageDescriptor(Type messageType, Type handlerType)
        {
            MessageType = messageType;
            HandlerType = handlerType;
        }

        public Type MessageType { get; }
        public Type HandlerType { get; }
    }
}