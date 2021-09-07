using System;

namespace LiteBus.Messaging.Abstractions.Descriptors
{
    public interface IPreHandlerDescriptor
    {
        Type PreHandlerType { get; }

        int Order { get; }

        Type MessageType { get; }
    }
}