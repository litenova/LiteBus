using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Represents an interface that defines an asynchronous pre-handler action to be executed before the primary message handling process.
/// </summary>
/// <typeparam name="TMessage">The type of the message to be handled, specifying the kind of messages that the pre-handler operates on.</typeparam>
/// <remarks>
/// Implementers of this interface should provide logic to dictate the actions to be performed asynchronously before the primary message handling process begins. This could include operations such as validation, logging, or transformation of the message to meet certain criteria or standards.
/// </remarks>
public interface IAsyncMessagePreHandler<in TMessage> : IMessagePreHandler<TMessage> where TMessage : notnull
{
    /// <summary>
    /// Defines the synchronous pre-handle method which internally calls the asynchronous pre-handle method, facilitating asynchronous pre-handle operations within a synchronous method signature. 
    /// </summary>
    /// <param name="message">The message to be pre-handled, constituting the input for the pre-handling operations performed asynchronously.</param>
    /// <returns>The outcome of the asynchronous pre-handling operation, potentially encapsulating modified versions of the message or other relevant data derived through the pre-handling process.</returns>
    object IMessagePreHandler<TMessage>.PreHandle(TMessage message)
    {
        return PreHandleAsync(message, AmbientExecutionContext.Current.CancellationToken);
    }

    /// <summary>
    /// Asynchronously handles preliminary actions to be conducted on the message before the primary handling process.
    /// </summary>
    /// <param name="message">The message that is to be pre-handled, serving as the input for any preparatory actions necessary before main handling.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to request the cancellation of the operation, allowing for the management of resource consumption through potential termination of the pre-handling process in necessary scenarios.</param>
    /// <returns>A task representing the asynchronous pre-handling operation, which, when completed, signifies the readiness of the message for the main handling process.</returns>
    Task PreHandleAsync(TMessage message, CancellationToken cancellationToken = default);
}