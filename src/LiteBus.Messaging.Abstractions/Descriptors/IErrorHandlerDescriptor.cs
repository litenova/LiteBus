using System;

namespace LiteBus.Messaging.Abstractions.Descriptors;

public interface IErrorHandlerDescriptor : IDescriptor
{
    Type ErrorHandlerType { get; }

    int Order { get; }
}