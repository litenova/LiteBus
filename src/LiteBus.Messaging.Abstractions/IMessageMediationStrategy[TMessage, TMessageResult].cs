using System.Collections.Generic;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a strategy for mediating messages of a specific type and producing results of a specific type.
/// </summary>
/// <typeparam name="TMessage">The type of message to be mediated.</typeparam>
/// <typeparam name="TMessageResult">The type of result produced by the mediation process.</typeparam>
/// <remarks>
///     Message mediation strategies encapsulate the logic for processing messages through the handling pipeline.
///     Different strategies can implement different patterns such as single handler execution, broadcast to multiple
///     handlers,
///     or more complex orchestration of handlers. The strategy determines how pre-handlers, main handlers, post-handlers,
///     and error handlers are invoked during the mediation process.
/// </remarks>
public interface IMessageMediationStrategy<in TMessage, out TMessageResult>
    where TMessage : notnull
{
    /// <summary>
    ///     Mediates a message by executing the appropriate handlers and producing a result.
    /// </summary>
    /// <param name="message">The message to be mediated.</param>
    /// <param name="messageDependencies">
    ///     The dependencies required for message handling, including handlers, pre-handlers,
    ///     post-handlers, and error handlers.
    /// </param>
    /// <param name="executionContext">
    ///     The context in which the mediation is executed, providing access to cancellation tokens,
    ///     shared data, and other execution-related information.
    /// </param>
    /// <returns>The result of the mediation process, of type <typeparamref name="TMessageResult" />.</returns>
    /// <remarks>
    ///     The implementation of this method defines the specific pattern for mediating messages, such as
    ///     executing a single handler, broadcasting to multiple handlers, or implementing more complex orchestration logic.
    ///     A result that is neither a <see cref="Task" /> nor an <see cref="IAsyncEnumerable{T}" /> is treated as already
    ///     materialized: its execution and dispatch scopes are disposed before <c>Mediate</c> returns, so do not return a
    ///     <c>ValueTask{TResult}</c> or any other value that is still in flight.
    /// </remarks>
    TMessageResult Mediate(TMessage message, IMessageDependencies messageDependencies, IExecutionContext executionContext);
}