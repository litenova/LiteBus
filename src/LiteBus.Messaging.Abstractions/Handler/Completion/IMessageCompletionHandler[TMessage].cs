using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Defines a completion handler for messages of type <typeparamref name="TMessage" />.
/// </summary>
/// <typeparam name="TMessage">The type of message this completion handler observes.</typeparam>
public interface IMessageCompletionHandler<TMessage> : IMessageCompletionHandler
    where TMessage : notnull
{
    /// <inheritdoc />
    Task IMessageCompletionHandler.HandleCompletionAsync(
        MessageCompletionContext context,
        CancellationToken cancellationToken)
    {
        return HandleCompletionAsync(context.AsTyped<TMessage>(), cancellationToken);
    }

    /// <summary>
    ///     Handles the end of a mediation operation for a message of type <typeparamref name="TMessage" />.
    /// </summary>
    /// <param name="context">The typed completion context observed at the end of mediation.</param>
    /// <param name="cancellationToken">The cancellation token supplied to the mediation operation.</param>
    /// <returns>A task representing the asynchronous completion-handling operation.</returns>
    Task HandleCompletionAsync(MessageCompletionContext<TMessage> context, CancellationToken cancellationToken);
}
