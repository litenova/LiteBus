using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an asynchronous message handler returning <typeparamref name="TMessageResult" />
/// </summary>
/// <typeparam name="TMessage">The message type</typeparam>
/// <typeparam name="TMessageResult">the message result type</typeparam>
public interface IAsyncHandler<in TMessage, TMessageResult> : IHandler<TMessage, Task<TMessageResult>>
{
    Task<TMessageResult> IHandler<TMessage, Task<TMessageResult>>.Handle(IHandleContext<TMessage> context)
    {
        return HandleAsync(context.Message, context.CancellationToken);
    }

    /// <summary>
    ///     Handles the <paramref name="message" /> asynchronously
    /// </summary>
    /// <param name="message">the message</param>
    /// <param name="cancellationToken">the cancellation token</param>
    /// <returns>A Task representing the message result</returns>
    Task<TMessageResult> HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}