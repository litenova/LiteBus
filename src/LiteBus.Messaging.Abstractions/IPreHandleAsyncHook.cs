using System.Threading;
using MorseCode.ITask;

namespace LiteBus.Messaging.Abstractions
{
    /// <summary>
    ///     Represents an action that is executed on <typeparamref name="TMessage"/> pre-handle phase
    /// </summary>
    /// <typeparam name="TMessage">The message type that is handled</typeparam>
    public interface IPreHandleAsyncHook<in TMessage> : IAsyncHook
    {
        ITask IAsyncHook.ExecuteAsync(object message, IHandleContext context)
        {
            return ExecuteAsync((TMessage)message, context.Data.Get<CancellationToken>());
        }

        ITask ExecuteAsync(TMessage message, CancellationToken cancellationToken);
    }
}