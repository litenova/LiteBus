using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a completion handler for messages of type <typeparamref name="TMessage" />.
/// </summary>
/// <typeparam name="TMessage">The type of message this completion handler observes.</typeparam>
/// <remarks>
///     The handler runs once for every mediation of the message, on every path: success, answer, denial, invalid
///     input, failure, and cancellation. Use <see cref="IMessageCompletionHandler{TMessage,TMessageResult}" /> when the message produces
///     a result the handler needs typed.
/// </remarks>
public interface IMessageCompletionHandler<TMessage> : IMessageCompletionHandler
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
    Task HandleCompletionAsync(MessageCompletionContext<TMessage> context, CancellationToken cancellationToken);
}
