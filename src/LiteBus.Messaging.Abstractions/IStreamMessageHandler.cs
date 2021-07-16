using System.Collections.Generic;
using System.Threading;

namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     Represents an asynchronous message handler that returns <see cref="IAsyncEnumerable{T}"/>
    /// </summary>
    /// <typeparam name="TMessage">The message type</typeparam>
    /// <typeparam name="TMessageResult">the message result type</typeparam>
    public interface IStreamMessageHandler<TMessage, out TMessageResult> : IMessageHandler<ICancellableMessage<TMessage>, IAsyncEnumerable<TMessageResult>>
    {
        IAsyncEnumerable<TMessageResult> IMessageHandler<ICancellableMessage<TMessage>, IAsyncEnumerable<TMessageResult>>.Handle(ICancellableMessage<TMessage> message)
        {
            return HandleAsync(message.Message, message.CancellationToken);
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