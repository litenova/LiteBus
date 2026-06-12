namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a contract for a message error handler that offers a mechanism to handle errors occurring during message
///     processing.
/// </summary>
public interface IMessageErrorHandler
{
    /// <summary>
    ///     Handles an error that has occurred during the processing of a message, offering a way to manage errors in a
    ///     centralized manner.
    /// </summary>
    /// <param name="context">The message, exception, and optional result observed when the error occurred.</param>
    /// <returns>
    ///     An object representing the outcome of the error handling. This can be used to convey information about the
    ///     handled error, possibly altering or enriching the initial error message with additional details.
    /// </returns>
    object HandleError(MessageErrorContext context);
}
