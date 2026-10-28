using System;
using System.Collections.Generic;
using System.Threading;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Mediates a message by invoking a single stream handler and yielding what it produces.
/// </summary>
/// <typeparam name="TMessage">Type of the message being handled.</typeparam>
/// <typeparam name="TMessageResult">Type of the items the handler streams.</typeparam>
/// <remarks>
///     <para>
///         This is the only mediation strategy that defers work until enumeration, and that is what makes it different
///         from the others rather than merely longer. Nothing happens until the caller enumerates, faults surface from
///         <c>MoveNextAsync</c> rather than from a call, and the completion stage fires when the enumerator is disposed.
///         A caller who never enumerates produces no completion record at all.
///     </para>
///     <para>
///         Faults are routed to the error stage from four places: acquiring the handler's stream, acquiring an
///         enumerator, advancing one, and running post-handlers. All four go through the same local function, because
///         four copies of that block is how they drift apart. A refusal mapper's stream is enumerated through the
///         same runner as the handler's stream, so a fault in it is routed rather than escaping raw. A fault the
///         recoverable filter excludes bypasses the error stage but is still recorded before completion runs.
///     </para>
///     <para>
///         The returned stream links the token supplied to <c>GetAsyncEnumerator</c> with the token captured
///         at mediation time, so a caller applying <c>WithCancellation</c> observes cancellation.
///     </para>
/// </remarks>
public sealed class SingleStreamHandlerMediationStrategy<TMessage, TMessageResult> :
    IMessageMediationStrategy<TMessage, IAsyncEnumerable<TMessageResult>>
    where TMessage : notnull
{
    /// <summary>
    ///     The cancellation token passed to stream enumeration during mediation.
    /// </summary>
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SingleStreamHandlerMediationStrategy{TMessage,TMessageResult}" />
    ///     class.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that will be used for stream enumeration.</param>
    public SingleStreamHandlerMediationStrategy(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     Mediates a message by executing the appropriate stream handler and yielding results asynchronously.
    /// </summary>
    /// <param name="message">The message to be mediated.</param>
    /// <param name="messageDependencies">
    ///     The dependencies required for message handling, including handlers, pre-stage handlers, post-handlers, and
    ///     error handlers.
    /// </param>
    /// <param name="executionContext">
    ///     The context in which the mediation is executed, providing access to cancellation tokens, shared data, and
    ///     other execution-related information.
    /// </param>
    /// <returns>An asynchronous stream of results produced by the handler.</returns>
    /// <exception cref="NoHandlerFoundException">Thrown when no handler is found for the message type.</exception>
    public IAsyncEnumerable<TMessageResult> Mediate(
        TMessage message,
        IMessageDependencies messageDependencies,
        IExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        return new SingleStreamHandlerMediationEnumerable<TMessage, TMessageResult>(
            message,
            messageDependencies,
            executionContext,
            _cancellationToken);
    }
}
