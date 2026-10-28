using System;
using System.ComponentModel;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a descriptor for a completion handler, providing metadata about the handler such as the message type it
///     observes, its execution order, and any associated tags.
/// </summary>
public interface ICompletionHandlerDescriptor : IHandlerDescriptor
{
    /// <summary>
    ///     Gets the result type the completion handler expects, when it was registered under a contract that names one.
    /// </summary>
    /// <remarks>
    ///     A completion handler may observe a message without caring about its result, in which case this is
    ///     <see langword="null" />.
    /// </remarks>
    Type? MessageResultType { get; }

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
