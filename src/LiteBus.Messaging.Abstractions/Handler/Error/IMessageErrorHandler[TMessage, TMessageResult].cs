using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a contract for a message error handler that operates on messages of specified types, providing mechanisms
///     for handling errors that occur during message processing.
/// </summary>
/// <typeparam name="TMessage">The type of the message that this handler can process.</typeparam>
/// <typeparam name="TMessageResult">The type of the message result that this handler deals with.</typeparam>
public interface IMessageErrorHandler<in TMessage, in TMessageResult> : IMessageErrorHandler where TMessage : notnull
{
    /// <summary>
    ///     Provides a default implementation for handling errors that occur during message processing. It casts the input
    ///     parameters to the expected types and calls the typed <see cref="HandleError" /> method.
    /// </summary>
    /// <param name="message">The message where the error occurred, to be cast to type <typeparamref name="TMessage" />.</param>
    /// <param name="exception">The exception that triggered the error.</param>
    /// <param name="messageResult">
    ///     The result of the message processing that led to the error, to be cast to type
    ///     <typeparamref name="TMessageResult" />.
    /// </param>
    /// <returns>The result of handling the error, which might be modified or enriched with additional details.</returns>
    object IMessageErrorHandler.HandleError(object message, Exception exception, object? messageResult)
    {
        return HandleError((TMessage) message, exception, (TMessageResult?) messageResult);
    }

    /// <summary>
    ///     Handles an error that occurred during the processing of a message, allowing for customized error handling
    ///     strategies to be implemented based on the types of the message and message result.
    /// </summary>
    /// <param name="message">The message that was being processed when the error occurred.</param>
    /// <param name="exception">The exception that triggered the error.</param>
    /// <param name="messageResult">The result of the message processing that led to the error, which can be null.</param>
    /// <returns>The result of handling the error, potentially modified or enriched with additional information.</returns>
    object HandleError(TMessage message, Exception exception, TMessageResult? messageResult);
}