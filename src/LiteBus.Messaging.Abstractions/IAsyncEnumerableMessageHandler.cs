using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     The base of all asynchronous handlers returning <see cref="IAsyncEnumerable{T}"/>
    /// </summary>
    public interface IAsyncEnumerableMessageHandler : IMessageHandler
    {
        /// <summary>
        ///     Handles a message asynchronously
        /// </summary>
        /// <param name="message">The message</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>An asynchronous enumerable representing the message result</returns>
        IAsyncEnumerable<object> HandleAsync(object message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    ///     Represents an asynchronous message handler that returns <see cref="IAsyncEnumerable{T}"/>
    /// </summary>
    /// <typeparam name="TMessage">The message type</typeparam>
    /// <typeparam name="TMessageResult">the message result type</typeparam>
    public interface IAsyncEnumerableMessageHandler<in TMessage, out TMessageResult> : IAsyncEnumerableMessageHandler
        where TMessage : IMessage
    {
        IAsyncEnumerable<object> IAsyncEnumerableMessageHandler.HandleAsync(
            object message, CancellationToken cancellationToken)
        {
            return HandleAsync((TMessage) message, cancellationToken) as IAsyncEnumerable<object>;
        }

        /// <summary>
        ///     Handles a message asynchronously
        /// </summary>
        /// <param name="message">the message</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> representing the collection of message results</returns>
        IAsyncEnumerable<TMessageResult> HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    }
}