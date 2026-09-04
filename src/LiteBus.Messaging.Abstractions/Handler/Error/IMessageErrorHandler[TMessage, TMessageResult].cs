using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an asynchronous error handler for messages of type <typeparamref name="TMessage" />
///     and results of type <typeparamref name="TMessageResult" />.
///     This interface should be implemented to handle exceptions that occur during the processing of messages.
/// </summary>
/// <typeparam name="TMessage">The type of the message that this error handler is applicable to.</typeparam>
/// <typeparam name="TMessageResult">The type of the result produced by the message processing.</typeparam>
public interface IMessageErrorHandler<TMessage, TMessageResult> : IMessageErrorHandler where TMessage : notnull
{
    /// <summary>
    ///     Adapts the untyped runtime context to the handler's typed context while preserving shared outcome state.
    /// </summary>
    /// <param name="context">The message, exception, and optional result observed when the error occurred.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous error-handling operation.</returns>
    Task IMessageErrorHandler.HandleErrorAsync(MessageErrorContext context, CancellationToken cancellationToken)
    {
        return HandleErrorAsync(context.AsTyped<TMessage, TMessageResult>(), cancellationToken);
    }

    /// <summary>
    ///     Asynchronously handles an error encountered in message processing.
    /// </summary>
    /// <param name="context">The typed error context whose outcome state is shared with the mediation pipeline.</param>
    /// <param name="cancellationToken">A token for cancelling the error handling operation.</param>
    /// <returns>A task representing the asynchronous error handling operation.</returns>
    Task HandleErrorAsync(
        MessageErrorContext<TMessage, TMessageResult> context,
        CancellationToken cancellationToken = default);
}
