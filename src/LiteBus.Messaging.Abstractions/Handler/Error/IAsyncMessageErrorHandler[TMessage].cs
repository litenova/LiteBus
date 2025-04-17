using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an asynchronous error handler for messages of type <typeparamref name="TMessage" />.
///     This interface should be implemented to handle exceptions that occur during message processing.
/// </summary>
/// <typeparam name="TMessage">The type of the message that this error handler is applicable to.</typeparam>
public interface IAsyncMessageErrorHandler<in TMessage> : IMessageErrorHandler<TMessage, object> where TMessage : notnull
{
    /// <summary>
    ///     Synchronously handles an error encountered in message processing by delegating to an asynchronous method.
    /// </summary>
    /// <param name="message">The message that encountered the error.</param>
    /// <param name="exception">The exception that was thrown during message processing.</param>
    /// <param name="messageResult">The result of the message processing prior to the error.</param>
    /// <returns>A placeholder object returned after handling the error.</returns>
    object IMessageErrorHandler<TMessage, object>.HandleError(TMessage message, Exception exception, object? messageResult)
    {
        return HandleErrorAsync(message, messageResult, exception, AmbientExecutionContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Asynchronously handles an error encountered in message processing.
    /// </summary>
    /// <param name="message">The message that encountered the error.</param>
    /// <param name="messageResult">The result of the message processing prior to the error.</param>
    /// <param name="exception">The exception that was thrown during message processing.</param>
    /// <param name="cancellationToken">A token for cancelling the error handling operation.</param>
    /// <returns>A task representing the asynchronous error handling operation.</returns>
    Task HandleErrorAsync(TMessage message, object? messageResult, Exception exception, CancellationToken cancellationToken = default);
}