using System.Threading;
using System.Threading.Tasks;

namespace Paykan.Messaging.Abstractions
{
    /// <summary>
    ///     The base of all message handlers
    /// </summary>
    public interface IMessageHandler
    {
    }

    /// <summary>
    ///     Represents a handler for a message
    /// </summary>
    /// <typeparam name="TMessage">the type of message</typeparam>
    /// <typeparam name="TMessageResult">the type of message</typeparam>
    /// <remarks>The message can be of any type</remarks>
    public interface IMessageHandler<in TMessage, out TMessageResult> : IMessageHandler
    {
        /// <summary>
        ///     Handles a message
        /// </summary>
        /// <param name="message">the message</param>
        /// <param name="cancellationToken">the cancellation token</param>
        /// <returns>the message result</returns>
        TMessageResult HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    ///     Represents a handle for a message return <see cref="Task" />
    /// </summary>
    /// <typeparam name="TMessage">the type of message</typeparam>
    /// <remarks>The message can be of any type</remarks>
    public interface IMessageHandler<in TMessage> : IMessageHandler<TMessage, Task>
    {
    }
}