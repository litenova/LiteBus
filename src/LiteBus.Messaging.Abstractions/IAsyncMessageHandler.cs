using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     Represents an asynchronous message handler with no result
    /// </summary>
    /// <typeparam name="TMessage">The message type</typeparam>
    public interface IAsyncMessageHandler<TMessage> : IMessageHandler<ICancellableMessage<TMessage>, Task>
    {
        Task IMessageHandler<ICancellableMessage<TMessage>, Task>.Handle(ICancellableMessage<TMessage> message)
        {
            return HandleAsync(message.Message, message.CancellationToken);
        }

        /// <summary>
        ///     Handles the <paramref name="message"/> asynchronously
        /// </summary>
        /// <param name="message">the message</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>A task representing the message result</returns>
        Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    ///     Represents an asynchronous message handler returning <typeparamref name="TMessageResult"/>
    /// </summary>
    /// <typeparam name="TMessage">The message type</typeparam>
    /// <typeparam name="TMessageResult">the message result type</typeparam>
    public interface IAsyncMessageHandler<TMessage, TMessageResult> : IMessageHandler<ICancellableMessage<TMessage>, Task<TMessageResult>>
    {
        Task<TMessageResult> IMessageHandler<ICancellableMessage<TMessage>, Task<TMessageResult>>.Handle(ICancellableMessage<TMessage> message)
        {
            return HandleAsync(message.Message, message.CancellationToken);
        }

        /// <summary>
        ///     Handles the <paramref name="message"/> asynchronously
        /// </summary>
        /// <param name="message">the message</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>A task representing the message result</returns>
        Task<TMessageResult> HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    }
}