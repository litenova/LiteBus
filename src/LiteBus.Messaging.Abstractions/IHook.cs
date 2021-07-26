using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each message post-handle phase
    /// </summary>
    public interface IHook<in TMessage>
    {
        /// <summary>
        ///     Executes the hook
        /// </summary>
        /// <param name="message">The message that is handled</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns></returns>
        Task ExecuteAsync(TMessage message, CancellationToken cancellationToken = default);
    }
}