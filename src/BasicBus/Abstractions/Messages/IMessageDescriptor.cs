using System;

namespace BasicBus.Abstractions
{
    public interface IMessageDescriptor
    {
        Type MessageType { get; }
        Type HandlerType { get; }
    }
}