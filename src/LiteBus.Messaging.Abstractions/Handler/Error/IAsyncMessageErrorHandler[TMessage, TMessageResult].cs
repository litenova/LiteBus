using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents an asynchronous error handler for messages of type <typeparamref name="TMessage"/>
/// and results of type <typeparamref name="TMessageResult"/>.
/// This interface should be implemented to handle exceptions that occur during the processing of messages.
/// </summary>
/// <typeparam name="TMessage">The type of the message that this error handler is applicable to.</typeparam>
/// <typeparam name="TMessageResult">The type of the result produced by the message processing.</typeparam>
public interface IAsyncMessageErrorHandler<in TMessage, in TMessageResult> : IMessageErrorHandler<TMessage, TMessageResult> where TMessage : notnull
{
    /// <summary>
    /// Synchronously handles an error encountered in message processing by delegating to an asynchronous method.
    /// </summary>
    /// <param name="message">The message that encountered the error.</param>
    /// <param name="exception">The exception that was thrown during message processing.</param>
    /// <param name="messageResult">The result of the message processing prior to the error, which may be null.</param>
    /// <returns>A placeholder object returned after handling the error.</returns>
    object IMessageErrorHandler<TMessage, TMessageResult>.HandleError(TMessage message, Exception exception, TMessageResult? messageResult)
    {
        return HandleErrorAsync(message, messageResult, exception, AmbientExecutionContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asynchronously handles an error encountered in message processing.
    /// </summary>
    /// <param name="message">The message that encountered the error.</param>
    /// <param name="messageResult">The result of the message processing prior to the error, which may be null.</param>
    /// <param name="exception">The exception that was thrown during message processing.</param>
    /// <param name="cancellationToken">A token for cancelling the error handling operation.</param>
    /// <returns>A task representing the asynchronous error handling operation.</returns>
    Task HandleErrorAsync(TMessage message, TMessageResult? messageResult, Exception exception, CancellationToken cancellationToken = default);
}