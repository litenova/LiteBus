using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

internal sealed class PostHandlerDescriptor : HandlerDescriptorBase, IPostHandlerDescriptor
{
    public required Type MessageResultType { get; init; }
}