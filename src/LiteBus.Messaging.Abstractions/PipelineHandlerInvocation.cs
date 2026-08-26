using System;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Invokes the pipeline stages whose contracts take a context object rather than the message itself.
/// </summary>
internal static class PipelineHandlerInvocation
{
    /// <summary>
    ///     Invokes an error handler asynchronously with the supplied cancellation token.
    /// </summary>
    /// <param name="handler">The error handler instance.</param>
    /// <param name="context">The error context observed during mediation.</param>
    /// <param name="cancellationToken">The cancellation token for the error handler invocation.</param>
    /// <returns>A task representing the asynchronous error handler operation.</returns>
    public static Task InvokeErrorHandlerAsync(
        IMessageErrorHandler handler,
        MessageErrorContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(context);
        return handler.HandleErrorAsync(context, cancellationToken);
    }

    /// <summary>
    ///     Invokes a completion handler asynchronously with the supplied cancellation token.
    /// </summary>
    /// <param name="handler">The completion handler instance.</param>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <param name="cancellationToken">The cancellation token for the completion handler invocation.</param>
    /// <returns>A task representing the asynchronous completion handler operation.</returns>
    public static Task InvokeCompletionHandlerAsync(
        IMessageCompletionHandler handler,
        MessageCompletionContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(context);
        return handler.HandleCompletionAsync(context, cancellationToken);
    }
}
