using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

internal sealed class MainHandlerDescriptor : HandlerDescriptorBase, IMainHandlerDescriptor
{
    public required Type MessageResultType { get; init; }
}