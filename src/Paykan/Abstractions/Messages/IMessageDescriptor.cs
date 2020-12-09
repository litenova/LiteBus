using System;
using System.Collections.Generic;

namespace Paykan.Abstractions
{
    public interface IMessageDescriptor
    {
        Type MessageType { get; }
        IReadOnlyCollection<Type> HandlerTypes { get; }
        IReadOnlyCollection<Type> PostHandleHookTypes { get; }
    }
}