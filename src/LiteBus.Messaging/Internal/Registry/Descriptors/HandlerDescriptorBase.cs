using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Internal.Registry.Descriptors;

internal abstract class HandlerDescriptorBase : IHandlerDescriptor
{
    public required Type MessageType { get; init; }

    public required int Order { get; init; }

    public required IReadOnlyCollection<string> Tags { get; init; }

    public required Type HandlerType { get; init; }
}