using System;

namespace Paykan.Abstractions
{
    public interface IMessageDescriptor
    {
        Type MessageType { get; }
        Type HandlerType { get; }
    }
}