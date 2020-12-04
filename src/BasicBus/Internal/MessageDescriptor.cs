using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using BasicBus.Abstractions;

namespace BasicBus.Internal
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