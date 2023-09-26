using System;

namespace LiteBus.Messaging.Abstractions;

public interface IErrorHandlerDescriptor : IDescriptor
{
    Type ErrorHandlerType { get; }

    int Order { get; }

    bool IsGeneric { get; set; }
}