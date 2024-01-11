using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Provides extension methods for running pre-handlers, error handlers, and post-handlers in the message handling process.
/// This class facilitates the execution of handler pipelines for messages, allowing for a structured and organized approach to message handling with pre-processing, error handling, and post-processing steps.
/// </summary>
public static class MessageContextExtensions
{
    /// <summary>
    /// Runs asynchronous pre-handlers for a given message, allowing for operations such as validation and logging to be performed before the primary message handling.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating pre-handlers.</param>
    /// <param name="message">The message to be pre-handled.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public static async Task RunAsyncPreHandlers(this IMessageDependencies messageDependencies, object message)
    {
        foreach (var preHandler in messageDependencies.IndirectPreHandlers)
        {
            await (Task) preHandler.Handler.Value.PreHandle(message);
        }

        foreach (var preHandler in messageDependencies.PreHandlers)
        {
            await (Task) preHandler.Handler.Value.PreHandle(message);
        }
    }

    /// <summary>
    /// Runs error handlers for a given context, allowing for centralized error handling logic to be applied in the case of failures during the message handling process.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating error handlers.</param>
    /// <param name="message">The message that was being handled when the error occurred.</param>
    /// <param name="messageResult">The result of the message handling process, if any.</param>
    /// <param name="exceptionDispatchInfo">The exception that triggered the error handler.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public static async Task RunAsyncErrorHandlers(this IMessageDependencies messageDependencies, object message, object messageResult, ExceptionDispatchInfo exceptionDispatchInfo)
    {
        if (messageDependencies.ErrorHandlers.Count + messageDependencies.IndirectErrorHandlers.Count == 0)
        {
            exceptionDispatchInfo.Throw();
        }

        foreach (var errorHandler in messageDependencies.IndirectErrorHandlers)
        {
            await (Task) errorHandler.Handler.Value.HandleError(message, exceptionDispatchInfo.SourceException, messageResult);
        }

        foreach (var errorHandler in messageDependencies.ErrorHandlers)
        {
            await (Task) errorHandler.Handler.Value.HandleError(message, exceptionDispatchInfo.SourceException, exceptionDispatchInfo);
        }
    }

    /// <summary>
    /// Runs post-handlers for a given context, allowing for operations such as logging and further processing to be performed after the primary message handling.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating post-handlers.</param>
    /// <param name="message">The message that has been handled.</param>
    /// <param name="messageResult">The result produced by the message handling process.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public static async Task RunAsyncPostHandlers(this IMessageDependencies messageDependencies, object message, object messageResult)
    {
        foreach (var postHandler in messageDependencies.PostHandlers)
        {
            await (Task) postHandler.Handler.Value.PostHandle(message, messageResult);
        }

        foreach (var postHandler in messageDependencies.IndirectPostHandlers)
        {
            await (Task) postHandler.Handler.Value.PostHandle(message, messageResult);
        }
    }
}