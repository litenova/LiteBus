using System.ComponentModel;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a descriptor for a pre-handler, providing metadata about the handler such as the message type it
///     handles, its execution order, and any associated tags.
/// </summary>
public interface IPreHandlerDescriptor : IHandlerDescriptor
{
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
