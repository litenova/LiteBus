using System;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

/// <summary>
///     Describes a refusal mapper registered for a message type.
/// </summary>
internal sealed class RefusalMapperDescriptor : HandlerDescriptorBase, IRefusalMapperDescriptor
{
    /// <inheritdoc />
    public required Type MessageResultType { get; init; }

    /// <inheritdoc />
    public PipelineDispatch? Dispatch { get; init; }
}
