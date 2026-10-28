using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a completion handler for messages of type <typeparamref name="TMessage" /> that receives the result typed.
/// </summary>
/// <typeparam name="TMessage">The type of message this completion handler observes.</typeparam>
/// <typeparam name="TMessageResult">The result type of the message.</typeparam>
/// <remarks>
///     The handler runs once for every mediation of the message, on every path. The result is present only on the paths
///     where the pipeline produced one, which the context reports through
///     <see cref="MessageCompletionContext{TMessage,TMessageResult}.HasResult" />.
/// </remarks>
public interface IMessageCompletionHandler<TMessage, TMessageResult> : IMessageCompletionHandler
    where TMessage : notnull
{
    /// <summary>
    ///     Handles the end of a mediation operation for a message of type <typeparamref name="TMessage" />.
    /// </summary>
    /// <param name="context">The typed completion context observed at the end of mediation.</param>
    /// <param name="cancellationToken">
    ///     The cancellation token for the completion stage. The stage is not cancellable, so this is
    ///     <see cref="CancellationToken.None" />; a handler that needs a deadline applies its own.
    /// </param>
    /// <returns>A task representing the asynchronous completion-handling operation.</returns>
    Task HandleCompletionAsync(
        MessageCompletionContext<TMessage, TMessageResult> context,
        CancellationToken cancellationToken);
}
