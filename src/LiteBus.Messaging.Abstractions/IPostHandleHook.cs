using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each message post-handle phase
    /// </summary>
    public interface IPostHandleHook
    {
        /// <summary>
        ///     Executes the hook
        /// </summary>
        /// <param name="message">The message that is handled</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        Task ExecuteAsync(object message, CancellationToken cancellationToken = default);
    }

    /// <summary>
    ///     Represents an action that is executed on <typeparamref name="TMessage"/> post-handle phase
    /// </summary>
    /// <typeparam name="TMessage">The message type that is handled</typeparam>
    public interface IPostHandleHook<in TMessage> : IPostHandleHook where TMessage : IMessage
    {
        Task IPostHandleHook.ExecuteAsync(object message, CancellationToken cancellationToken) =>
            ExecuteAsync((TMessage) message, cancellationToken);

        /// <summary>
        ///     Executes the hook
        /// </summary>
        /// <param name="message">The message that is handled</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        Task ExecuteAsync(TMessage message, CancellationToken cancellationToken = default);
    }
}