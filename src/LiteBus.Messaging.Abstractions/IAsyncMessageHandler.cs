using System.Threading;
using MorseCode.ITask;

namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     Represents an asynchronous message handler with no result
    /// </summary>
    /// <typeparam name="TMessage">The message type</typeparam>
    public interface IAsyncMessageHandler<in TMessage> : IMessageHandler<ICancellableMessage<TMessage>, ITask>
    {
        ITask IMessageHandler<ICancellableMessage<TMessage>, ITask>.Handle(ICancellableMessage<TMessage> message)
        {
            return HandleAsync(message.Message, message.CancellationToken);
        }

        /// <summary>
        ///     Handles the <paramref name="message"/> asynchronously
        /// </summary>
        /// <param name="message">the message</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>A task representing the message result</returns>
        ITask HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    ///     Represents an asynchronous message handler returning <typeparamref name="TMessageResult"/>
    /// </summary>
    /// <typeparam name="TMessage">The message type</typeparam>
    /// <typeparam name="TMessageResult">the message result type</typeparam>
    public interface IAsyncMessageHandler<in TMessage, out TMessageResult> : IMessageHandler<ICancellableMessage<TMessage>, ITask<TMessageResult>>
    {
        ITask<TMessageResult> IMessageHandler<ICancellableMessage<TMessage>, ITask<TMessageResult>>.Handle(ICancellableMessage<TMessage> message)
        {
            return HandleAsync(message.Message, message.CancellationToken);
        }

        /// <summary>
        ///     Handles the <paramref name="message"/> asynchronously
        /// </summary>
        /// <param name="message">the message</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>A task representing the message result</returns>
        ITask<TMessageResult> HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    }
}