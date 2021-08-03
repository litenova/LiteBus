using System;

namespace LiteBus.Messaging.Abstractions.Descriptors
{
    public interface IHandlerDescriptor
    {
        Type HandlerType { get; }

        Type MessageType { get; }

        Type MessageResultType { get; }
    }
}