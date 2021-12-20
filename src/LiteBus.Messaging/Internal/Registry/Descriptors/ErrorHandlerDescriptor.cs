using System;
using LiteBus.Messaging.Abstractions.Descriptors;

namespace LiteBus.Messaging.Internal.Registry.Descriptors
{
    internal class ErrorHandlerDescriptor : IErrorHandlerDescriptor
    {
        public ErrorHandlerDescriptor(Type errorHandlerType, Type messageType, int order)
        {
            ErrorHandlerType = errorHandlerType;
            Order = order;
            MessageType = messageType;
        }

        public Type ErrorHandlerType { get; }

        public int Order { get; }

        public Type MessageType { get; }
    }
}