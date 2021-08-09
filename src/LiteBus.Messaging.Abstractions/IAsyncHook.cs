using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on each message post-handle or pre-handle phase
    /// </summary>
    public interface IAsyncHook
    {
        /// <summary>
        ///     Executes the hook
        /// </summary>
        /// <param name="message">The message that is being handled</param>
        /// <param name="context">The handle context</param>
        Task ExecuteAsync(object message, IHandleContext context);
    }
}