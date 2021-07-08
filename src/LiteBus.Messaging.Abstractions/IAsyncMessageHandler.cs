using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     The base of all asynchronous handlers
    /// </summary>
    public interface IAsyncMessageHandler : IMessageHandler
    {
        /// <summary>
        ///     Handles a message
        /// </summary>
        /// <param name="message">The message</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>A task representing the message result</returns>
        Task HandleAsync(object message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    ///     Represents an asynchronous message handler returning <typeparamref name="TMessageResult"/>
    /// </summary>
    /// <typeparam name="TMessage">The message type</typeparam>
    /// <typeparam name="TMessageResult">the message result type</typeparam>
    public interface IAsyncMessageHandler<in TMessage, TMessageResult> : IAsyncMessageHandler where TMessage : IMessage
    {
        Task IAsyncMessageHandler.HandleAsync(object message, CancellationToken cancellationToken)
        {
            return HandleAsync((TMessage) message, cancellationToken);
        }

        /// <summary>
        ///     Handles the <paramref name="message"/> asynchronously
        /// </summary>
        /// <param name="message">the message</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>A task representing the message result</returns>
        Task<TMessageResult> HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    ///     Represents an asynchronous message handler with no result
    /// </summary>
    /// <typeparam name="TMessage">The message type</typeparam>
    public interface IAsyncMessageHandler<in TMessage> : IAsyncMessageHandler where TMessage : IMessage
    {
        Task IAsyncMessageHandler.HandleAsync(object message, CancellationToken cancellationToken)
        {
            return HandleAsync((TMessage) message, cancellationToken);
        }

        /// <summary>
        ///     Handles the <paramref name="message"/> asynchronously
        /// </summary>
        /// <param name="message">the message</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>A task representing the message result</returns>
        Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    }
}