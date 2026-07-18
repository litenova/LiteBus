using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a contract for a message error handler that offers a mechanism to handle errors occurring during message
///     processing.
/// </summary>
public interface IMessageErrorHandler
{
    /// <summary>
    ///     Asynchronously handles an error that occurred during message processing.
    /// </summary>
    /// <param name="context">The message, exception, and optional result observed when the error occurred.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous error-handling operation.</returns>
    Task HandleErrorAsync(MessageErrorContext context, CancellationToken cancellationToken);
}
