using System;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Defines a contract for a message error handler that offers a mechanism to handle errors occurring during message processing.
/// </summary>
public interface IMessageErrorHandler
{
    /// <summary>
    /// Handles an error that has occurred during the processing of a message, offering a way to manage errors in a centralized manner.
    /// </summary>
    /// <param name="message">The message that was being processed when the error occurred. This object represents the message that caused the error.</param>
    /// <param name="exception">The exception that triggered the error. This object represents the exception that caused the error.</param>
    /// <param name="messageResult">The result of the message processing that triggered the error. This object represents any output or state at the time of the error.</param>
    /// <returns>An object representing the outcome of the error handling. This can be used to convey information about the handled error, possibly altering or enriching the initial error message with additional details.</returns>
    object HandleError(object message, Exception exception, object messageResult);
}