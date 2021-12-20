using System;

namespace LiteBus.Messaging.Abstractions.Descriptors
{
    public interface IPreHandlerDescriptor : IDescriptor
    {
        Type PreHandlerType { get; }

        int Order { get; }
    }
}