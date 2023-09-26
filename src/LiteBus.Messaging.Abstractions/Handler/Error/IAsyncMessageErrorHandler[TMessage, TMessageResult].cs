using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Defines a contract for an asynchronous message error handler that operates on messages of type <typeparamref name="TMessage"/> and message results of type <typeparamref name="TMessageResult"/>.
/// </summary>
/// <typeparam name="TMessage">The type of the message that this handler can process.</typeparam>
/// <typeparam name="TMessageResult">The type of the message result that this handler deals with.</typeparam>
public interface IAsyncMessageErrorHandler<in TMessage, in TMessageResult> : IMessageErrorHandler<TMessage, TMessageResult>
{
    /// <summary>
    /// Synchronously handles an error occurring during message processing using the default error handling strategy, by internally calling the asynchronous <see cref="HandleErrorAsync(TMessage, TMessageResult, CancellationToken)"/> method.
    /// </summary>
    /// <param name="message">The message that was being processed when the error occurred.</param>
    /// <param name="exception"></param>
    /// <param name="messageResult">The result of the message processing that led to the error, of type <typeparamref name="TMessageResult"/>.</param>
    /// <returns>The result of handling the error, potentially modified or enriched with additional information.</returns>
    object IMessageErrorHandler<TMessage, TMessageResult>.HandleError(TMessage message, Exception exception, TMessageResult messageResult)
    {
        return HandleErrorAsync(message, messageResult, AmbientExecutionContext.Current?.CancellationToken ?? throw new NoExecutionContextException());
    }

    /// <summary>
    /// Asynchronously handles an error that occurred during the processing of a message, potentially altering the result based on the error handling logic implemented.
    /// </summary>
    /// <param name="message">The message that was being processed when the error occurred, of type <typeparamref name="TMessage"/>.</param>
    /// <param name="messageResult">The result of the message processing that led to the error, of type <typeparamref name="TMessageResult"/>.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests, allowing this method to be exited prematurely if necessary. Defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation, potentially altering the message result based on the error handling logic implemented.</returns>
    Task HandleErrorAsync(TMessage message, TMessageResult messageResult, CancellationToken cancellationToken = default);
}