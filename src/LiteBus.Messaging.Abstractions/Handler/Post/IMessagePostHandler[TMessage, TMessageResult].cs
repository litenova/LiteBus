using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents a post-handler that runs after a message of type <typeparamref name="TMessage" /> has been handled.
/// </summary>
/// <typeparam name="TMessage">The type of the message that was handled.</typeparam>
/// <typeparam name="TMessageResult">The type of the result produced by the main handler.</typeparam>
/// <remarks>
///     A post-handler may replace what the caller receives by writing to
///     <see cref="IExecutionContext.MessageResult" />, and may skip the post-handlers that have not run yet by calling
///     <see cref="IExecutionContext.SuppressPostHandlers" />.
/// </remarks>
public interface IMessagePostHandler<in TMessage, in TMessageResult> : IMessagePostHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Runs after the main handler has produced a result.
    /// </summary>
    /// <param name="message">The message that was handled.</param>
    /// <param name="messageResult">The result produced by the main handler, when any.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous post-handling operation.</returns>
    Task PostHandleAsync(TMessage message, TMessageResult? messageResult, CancellationToken cancellationToken = default);
}
