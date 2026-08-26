using System;
using System.ComponentModel;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a descriptor for a post-handler, providing metadata about the handler such as the message type it
///     handles, its execution order, any associated tags, and the type of the result that is expected by the user as an
///     argument.
/// </summary>
public interface IPostHandlerDescriptor : IHandlerDescriptor
{
    /// <summary>
    ///     Gets the type of the result produced by the main handler that is associated with this post-handler.
    /// </summary>
    Type MessageResultType { get; }

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
