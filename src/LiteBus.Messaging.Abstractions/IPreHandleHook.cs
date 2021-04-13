using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     Allows for performing an action before the message is handled
    /// </summary>
    public interface IPreHandleHook
    {
        /// <summary>
        ///     Executes the hook
        /// </summary>
        /// <param name="message">The message that is handled</param>
        /// <returns></returns>
        Task ExecuteAsync(object message);
    }
    
    /// <summary>
    ///     Allows for performing an action before the message is handled
    /// </summary>
    /// <typeparam name="TMessage">The message type that is handled</typeparam>
    public interface IPreHandleHook<in TMessage> : IPreHandleHook where TMessage : IMessage
    {
        Task IPreHandleHook.ExecuteAsync(object message) => ExecuteAsync((TMessage) message);

        /// <summary>
        ///     Executes the hook
        /// </summary>
        /// <param name="message">The message that is handled</param>
        /// <returns></returns>
        Task ExecuteAsync(TMessage message);
    }
}