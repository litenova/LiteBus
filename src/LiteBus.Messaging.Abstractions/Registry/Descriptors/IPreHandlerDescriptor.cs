using System.ComponentModel;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a descriptor for a pre-handler, providing metadata about the handler such as the message type it
///     handles, which decision stage runs it, its execution order, and any associated tags.
/// </summary>
public interface IPreHandlerDescriptor : IHandlerDescriptor
{
    /// <summary>
    ///     Gets the decision stage that runs this handler.
    /// </summary>
    /// <remarks>
    ///     Guards, shortcuts, and plain pre-handlers share this descriptor kind, and the stage is what separates them.
    ///     The stage comes from the contract the handler was registered under, so it is known even for a handler
    ///     registered for a generic message, whose dispatch cannot be bound until the runtime message type is.
    /// </remarks>
    PipelineStage Stage { get; }

    /// <summary>
    ///     Gets the dispatch bound to <see cref="IHandlerDescriptor.ContractType" /> during registration.
    /// </summary>
    /// <remarks>
    ///     This is a framework hook. It is <see langword="null" /> when the contract was still open at registration,
    ///     which happens for a handler registered for a generic message; the pipeline binds those on first dispatch.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    PipelineDispatch? Dispatch { get; }
}
