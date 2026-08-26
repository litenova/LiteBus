using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a handler that runs when a mediation operation ends, whatever the outcome.
/// </summary>
/// <remarks>
///     <para>
///         Completion handlers close the gap left by post-handlers and error handlers. Post-handlers run only when the
///         main handler succeeds, and error handlers run only for recoverable exceptions. A completion handler runs on
///         every path, exactly once, which makes it the only stage that can record how a message actually ended.
///     </para>
///     <para>
///         A completion handler observes; it cannot change the outcome. An exception raised by a completion handler is
///         suppressed when the pipeline is already failing, so that an observer bug never replaces the original fault.
///     </para>
/// </remarks>
public interface IMessageCompletionHandler
{
    /// <summary>
    ///     Handles the end of a mediation operation.
    /// </summary>
    /// <param name="context">The message, outcome, and optional result or exception observed at the end of mediation.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous completion-handling operation.</returns>
    Task HandleCompletionAsync(MessageCompletionContext context, CancellationToken cancellationToken);
}
