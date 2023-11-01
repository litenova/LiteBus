using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents an asynchronous action executed after a message of the specified type has been handled, facilitating the creation of post-processing logic that operates asynchronously.
/// </summary>
/// <typeparam name="TMessage">The type of the message being handled, specifying the structure that the message should adhere to in the post-handling process.</typeparam>
/// <remarks>
/// Implementers of this interface should provide logic to dictate the actions to be executed asynchronously following the primary handling of the message, potentially utilizing and modifying the results of the initial handling process.
/// </remarks>
public interface IAsyncMessagePostHandler<in TMessage> : IMessagePostHandler<TMessage, object>
{
    /// <summary>
    /// Provides a synchronous wrapper over the asynchronous <see cref="PostHandleAsync"/> method, facilitating the post-handling process within a synchronous context by invoking the asynchronous method with the current ambient execution context.
    /// </summary>
    /// <param name="message">The message that has been handled, representing the input for the post-handling process.</param>
    /// <param name="messageResult">The result generated from the initial handling of the message, offering a basis for the post-handling process.</param>
    /// <returns>An object that potentially embodies further processing results or a modified version of the initial message result, encapsulating the outcome of the asynchronous post-handling operation.</returns>
    object IMessagePostHandler<TMessage, object>.PostHandle(TMessage message, object messageResult)
    {
        return PostHandleAsync(message, messageResult, AmbientExecutionContext.Current?.CancellationToken ?? throw new NoExecutionContextException());
    }

    /// <summary>
    /// Defines an operation to execute asynchronously after the message has been handled, enabling the implementation of asynchronous post-processing logic that can operate over the handled message.
    /// </summary>
    /// <param name="message">The handled message, serving as the input for any asynchronous post-processing actions.</param>
    /// <param name="messageResult"></param>
    /// <param name="cancellationToken">A token that can be used to request cancellation of the asynchronous operation, facilitating the graceful termination of the post-handling process upon cancellation requests.</param>
    /// <returns>A task representing the ongoing asynchronous post-handling operation, potentially culminating in further processing results or a modified message outcome.</returns>
    Task PostHandleAsync(TMessage message, object messageResult, CancellationToken cancellationToken = default);
}