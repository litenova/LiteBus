using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Defines a contract for an asynchronous message error handler that operates on messages of type <typeparamref name="TMessage"/>.
/// </summary>
/// <typeparam name="TMessage">The type of the message that this handler can process.</typeparam>
public interface IAsyncMessageErrorHandler<in TMessage> : IMessageErrorHandler<TMessage, object>
{
    /// <summary>
    /// Synchronously handles an error occurring during message processing using the default error handling strategy, by internally calling the asynchronous <see cref="HandleErrorAsync(TMessage, CancellationToken)"/> method.
    /// </summary>
    /// <param name="message">The message that was being processed when the error occurred.</param>
    /// <param name="exception"></param>
    /// <param name="messageResult">The result of the message processing that led to the error, if any.</param>
    /// <returns>The result of handling the error, potentially modified or enriched with additional information.</returns>
    object IMessageErrorHandler<TMessage, object>.HandleError(TMessage message, Exception exception, object messageResult)
    {
        return HandleErrorAsync(message, AmbientExecutionContext.Current?.CancellationToken ?? throw new NoExecutionContextException());
    }

    /// <summary>
    /// Asynchronously handles an error that occurred during the processing of a message.
    /// </summary>
    /// <param name="message">The message that was being processed when the error occurred.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests, allowing this method to be exited prematurely if necessary. Defaults to <see cref="CancellationToken.None"/>.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    Task HandleErrorAsync(TMessage message, CancellationToken cancellationToken = default);
}