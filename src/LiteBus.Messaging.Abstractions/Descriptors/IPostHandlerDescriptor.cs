using System;

namespace LiteBus.Messaging.Abstractions.Descriptors
{
    public interface IPostHandlerDescriptor
    {
        Type PostHandlerType { get; }

        int Order { get; }

        Type MessageType { get; }
        
        Type MessageResultType { get; }
    }
}