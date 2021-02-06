using System.Threading.Tasks;

namespace Paykan.Messaging.Abstractions
{
    /// <summary>
    ///     Allows for performing an action after the message is handled
    /// </summary>
    /// <typeparam name="TMessage">The message type that is handled</typeparam>
    public interface IPostHandleHook<in TMessage> where TMessage : IMessage
    {
        /// <summary>
        ///     Executes the hook
        /// </summary>
        /// <param name="message">The message that is handled</param>
        /// <returns></returns>
        Task ExecuteAsync(TMessage message);
    }
}