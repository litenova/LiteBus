using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

/// <summary>
///     Describes a post-handler registered for a message type.
/// </summary>
internal sealed class PostHandlerDescriptor : HandlerDescriptorBase, IPostHandlerDescriptor
{
    /// <inheritdoc />
    public required Type MessageResultType { get; init; }
}
