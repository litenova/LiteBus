using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

/// <summary>
///     Describes a main handler registered for a message type.
/// </summary>
internal sealed class MainHandlerDescriptor : HandlerDescriptorBase, IMainHandlerDescriptor
{
    /// <inheritdoc />
    public required Type MessageResultType { get; init; }
}
