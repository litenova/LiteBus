using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Represents an asynchronous post-handler that acts upon messages of a specified type, facilitating additional
///     operations or transformations to be conducted asynchronously on the results produced from the primary message
///     handling process.
/// </summary>
/// <typeparam name="TMessage">
///     The type of the message being handled, defining the structural context for the message
///     involved in the post-handling process.
/// </typeparam>
/// <typeparam name="TMessageResult">
///     The type of the result produced from the initial handling of the message, informing
///     the kind of data that can be worked upon in the asynchronous post-handling process.
/// </typeparam>
/// <remarks>
///     Implementers of this interface should focus on devising logic for actions to be executed asynchronously after the
///     primary handling of a message, which may involve working with or transforming the initial message results to
///     fulfill specified post-handling objectives.
/// </remarks>
public interface IAsyncMessagePostHandler<in TMessage, in TMessageResult> : IMessagePostHandler<TMessage, TMessageResult> where TMessage : notnull where TMessageResult : notnull
{
    /// <summary>
    ///     Provides a synchronous bridge to the asynchronous
    ///     <see cref="PostHandleAsync(TMessage, TMessageResult, CancellationToken)" /> method, utilizing the ambient execution
    ///     context to enable synchronous invocation of the asynchronous post-handling method.
    /// </summary>
    /// <param name="message">
    ///     The message that has been handled, imparting the context and data needed for the post-handling
    ///     actions.
    /// </param>
    /// <param name="messageResult">
    ///     The result generated through the primary handling of the message, serving as a basis for
    ///     any post-handling operations or transformations.
    /// </param>
    /// <returns>
    ///     An object embodying the outcomes or transformed results from the asynchronous post-handling process, which may
    ///     encompass further processing results or modifications to the initial message result.
    /// </returns>
    object IMessagePostHandler<TMessage, TMessageResult>.PostHandle(TMessage message, TMessageResult? messageResult)
    {
        return PostHandleAsync(message, messageResult, AmbientExecutionContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Orchestrates the asynchronous execution of actions after the message has been handled, allowing for the
    ///     implementation of post-processing logic that operates over both the handled message and its initial result with the
    ///     flexibility of asynchronous programming.
    /// </summary>
    /// <param name="message">
    ///     The handled message, which is the focal point for the post-handling actions, offering a
    ///     structured data set for asynchronous operations.
    /// </param>
    /// <param name="messageResult">
    ///     The initial result produced after handling the message, presenting a defined structure that
    ///     can be utilized in the asynchronous post-processing steps.
    /// </param>
    /// <param name="cancellationToken">
    ///     A token facilitating the cancellation of the asynchronous operation, enabling the
    ///     prevention of potential resource wastage through the halting of the operation upon cancellation requests.
    /// </param>
    /// <returns>
    ///     A task symbolizing the ongoing asynchronous post-handling operation, potentially yielding further processing
    ///     results or alterations to the initial message result.
    /// </returns>
    Task PostHandleAsync(TMessage message, TMessageResult? messageResult, CancellationToken cancellationToken = default);
}