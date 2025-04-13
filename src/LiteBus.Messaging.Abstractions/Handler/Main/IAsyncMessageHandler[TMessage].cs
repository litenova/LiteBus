using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents an interface for handling messages asynchronously without producing a specific result.
/// </summary>
/// <typeparam name="TMessage">Specifies the type of messages that this handler is capable of handling.</typeparam>
/// <remarks>
/// Implementations of this interface should aim to process messages in an asynchronous manner, performing necessary operations and actions on the received messages without a requirement to return a specific result aside from signaling the completion of the handling process.
/// </remarks>
public interface IAsyncMessageHandler<in TMessage> : IMessageHandler<TMessage, Task> where TMessage : notnull
{
    /// <summary>
    /// Implements the synchronous handling method defined in the <see cref="IMessageHandler{TMessage, Task}"/> interface by invoking the asynchronous handling method with the current ambient execution context's cancellation token.
    /// </summary>
    /// <param name="message">The message to be handled.</param>
    /// <returns>A task representing the asynchronous handling operation.</returns>
    /// <remarks>
    /// This method facilitates the seamless integration of synchronous and asynchronous message handling by internally invoking the asynchronous handler within the synchronous handling method, thus providing a unified approach to message handling.
    /// </remarks>
    Task IMessageHandler<TMessage, Task>.Handle(TMessage message)
    {
        return HandleAsync(message, AmbientExecutionContext.Current.CancellationToken);
    }

    /// <summary>
    /// Defines a method to handle messages asynchronously.
    /// </summary>
    /// <param name="message">The message to be handled.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the handling operation if necessary.</param>
    /// <returns>A task representing the asynchronous handling operation, which upon completion indicates that the message has been handled.</returns>
    /// <remarks>
    /// Implementers should provide the actual handling logic within this method, implementing the necessary asynchronous operations to process the message appropriately.
    /// </remarks>
    Task HandleAsync(TMessage message, CancellationToken cancellationToken = default);
}