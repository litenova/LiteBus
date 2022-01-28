using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an asynchronous message handler with no result
/// </summary>
/// <typeparam name="TMessage">The message type</typeparam>
public interface IAsyncMessageHandler<in TMessage> : IMessageHandler<TMessage, Task>
{
    Task IMessageHandler<TMessage, Task>.Handle(IHandleContext<TMessage> context)
    {
        return HandleAsync(context.Message, context.CancellationToken);
    }

    /// <summary>
    ///     Handles the <paramref name="message" /> asynchronously
    /// </summary>
    /// <param name="message">the message</param>
    /// <param name="cancellationToken">the cancellation token</param>
    /// <returns>A Task representing the message result</returns>
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}