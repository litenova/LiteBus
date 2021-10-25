using System;

namespace LiteBus.Messaging.Abstractions.Descriptors
{
    public interface IErrorHandlerDescriptor
    {
        Type ErrorHandlerType { get; }

        int Order { get; }

        Type MessageType { get; }
    }
}