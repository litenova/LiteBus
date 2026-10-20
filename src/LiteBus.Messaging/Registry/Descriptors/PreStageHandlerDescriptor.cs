using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.Registry.Descriptors;

/// <summary>
///     Describes a pre-handler, guard, or shortcut registered for a message type.
/// </summary>
internal sealed class PreStageHandlerDescriptor : HandlerDescriptorBase, IPreStageHandlerDescriptor
{
    /// <inheritdoc />
    public PreStage Stage { get; init; }

    /// <inheritdoc />
    public PipelineDispatch? Dispatch { get; init; }
}
