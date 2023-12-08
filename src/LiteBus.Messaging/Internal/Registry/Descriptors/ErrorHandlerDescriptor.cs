using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Registry.Descriptors;

internal sealed class ErrorHandlerDescriptor : HandlerDescriptorBase, IErrorHandlerDescriptor
{
    public required Type MessageResultType { get; init; }
}