using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Marks a type as an error handler so the message registry can discover it, and carries the untyped entry point the
///     pipeline dispatches through.
/// </summary>
/// <remarks>
///     Implement <see cref="IMessageErrorHandler{TMessage,TMessageResult}" />, which names the message type and supplies
///     this member through a default implementation. The error stage holds a single role, so the marker and the role
///     share a name; the pre stage holds four, which is why its family carries the separate name
///     <see cref="IMessagePreStageHandler" />.
/// </remarks>
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
