using System;
using System.Collections.Generic;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

/// <summary>
///     Base implementation shared by concrete handler descriptors.
/// </summary>
internal abstract class HandlerDescriptorBase : IHandlerDescriptor
{
    /// <inheritdoc />
    public required Type MessageType { get; init; }

    /// <inheritdoc />
    public required int Priority { get; init; }

    /// <inheritdoc />
    public required IReadOnlyCollection<string> Tags { get; init; }

    /// <inheritdoc />
    public required Type HandlerType { get; init; }
}
