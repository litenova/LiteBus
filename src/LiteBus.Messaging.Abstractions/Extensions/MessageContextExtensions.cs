using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Provides extension methods for running pre-handlers, error handlers, and post-handlers in the message handling
///     process.
///     This class facilitates the execution of handler pipelines for messages, allowing for a structured and organized
///     approach to message handling with pre-processing, error handling, and post-processing steps.
/// </summary>
public static class MessageContextExtensions
{
    /// <summary>
    ///     Runs asynchronous pre-handlers for a given message, allowing for operations such as validation and logging to be
    ///     performed before the primary message handling.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating pre-handlers.</param>
    /// <param name="message">The message to be pre-handled.</param>
    /// <param name="cancellationToken">The cancellation token passed to each pre-handler invocation.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public static async Task RunAsyncPreHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        CancellationToken cancellationToken)
    {
        foreach (var preHandler in messageDependencies.IndirectPreHandlers)
        {
            await InvokePreHandlerAsync(preHandler.Handler.Value, message, cancellationToken).ConfigureAwait(false);
        }

        foreach (var preHandler in messageDependencies.PreHandlers)
        {
            await InvokePreHandlerAsync(preHandler.Handler.Value, message, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs error handlers for a given context, allowing for centralized error handling logic to be applied in the case of
    ///     failures during the message handling process.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating error handlers.</param>
    /// <param name="message">The message that was being handled when the error occurred.</param>
    /// <param name="messageResult">The result of the message handling process, if any.</param>
    /// <param name="exceptionDispatchInfo">The exception that triggered the error handler.</param>
    /// <param name="cancellationToken">The cancellation token passed to each error handler invocation.</param>
    /// <returns>
    ///     The error context after all error handlers run. When <see cref="MessageErrorContext.Outcome" /> remains
    ///     <see cref="MessageErrorOutcome.Unhandled" />, the original exception is rethrown.
    /// </returns>
    public static async Task<MessageErrorContext> RunAsyncErrorHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        object? messageResult,
        ExceptionDispatchInfo exceptionDispatchInfo,
        CancellationToken cancellationToken)
    {
        if (messageDependencies.ErrorHandlers.Count + messageDependencies.IndirectErrorHandlers.Count == 0)
        {
            exceptionDispatchInfo.Throw();
        }

        var context = new MessageErrorContext
        {
            Message = message,
            Exception = exceptionDispatchInfo.SourceException,
            MessageResult = messageResult
        };

        foreach (var errorHandler in messageDependencies.IndirectErrorHandlers)
        {
            await InvokeErrorHandlerAsync(errorHandler.Handler.Value, context, cancellationToken).ConfigureAwait(false);
        }

        foreach (var errorHandler in messageDependencies.ErrorHandlers)
        {
            await InvokeErrorHandlerAsync(errorHandler.Handler.Value, context, cancellationToken).ConfigureAwait(false);
        }

        if (context.Outcome == MessageErrorOutcome.Unhandled)
        {
            exceptionDispatchInfo.Throw();
        }

        return context;
    }

    /// <summary>
    ///     Runs post-handlers for a given context, allowing for operations such as logging and further processing to be
    ///     performed after the primary message handling.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating post-handlers.</param>
    /// <param name="message">The message that has been handled.</param>
    /// <param name="messageResult">The result produced by the message handling process.</param>
    /// <param name="cancellationToken">The cancellation token passed to each post-handler invocation.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    public static async Task RunAsyncPostHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        object? messageResult,
        CancellationToken cancellationToken)
    {
        foreach (var postHandler in messageDependencies.PostHandlers)
        {
            await InvokePostHandlerAsync(
                    postHandler.Handler.Value,
                    message,
                    messageResult,
                    postHandler.Descriptor.MessageResultType,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var postHandler in messageDependencies.IndirectPostHandlers)
        {
            await InvokePostHandlerAsync(
                    postHandler.Handler.Value,
                    message,
                    messageResult,
                    postHandler.Descriptor.MessageResultType,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Invokes a pre-handler using an explicit cancellation token when an asynchronous method is available.
    /// </summary>
    /// <param name="handler">The pre-handler instance.</param>
    /// <param name="message">The message to pre-handle.</param>
    /// <param name="cancellationToken">The cancellation token for the invocation.</param>
    /// <returns>A task representing the asynchronous pre-handler operation.</returns>
    private static Task InvokePreHandlerAsync(
        IMessagePreHandler handler,
        object message,
        CancellationToken cancellationToken)
    {
        return PipelineHandlerInvocation.InvokePreHandlerAsync(handler, message, cancellationToken);
    }

    /// <summary>
    ///     Invokes a post-handler using an explicit cancellation token when an asynchronous method is available.
    /// </summary>
    /// <param name="handler">The post-handler instance.</param>
    /// <param name="message">The handled message.</param>
    /// <param name="messageResult">The result produced by the main handler, if any.</param>
    /// <param name="messageResultType">The declared result type associated with the post-handler.</param>
    /// <param name="cancellationToken">The cancellation token for the invocation.</param>
    /// <returns>A task representing the asynchronous post-handler operation.</returns>
    private static Task InvokePostHandlerAsync(
        IMessagePostHandler handler,
        object message,
        object? messageResult,
        Type messageResultType,
        CancellationToken cancellationToken)
    {
        return PipelineHandlerInvocation.InvokePostHandlerAsync(
            handler,
            message,
            messageResult,
            messageResultType,
            cancellationToken);
    }

    /// <summary>
    ///     Invokes an error handler using an explicit cancellation token when an asynchronous method is available.
    /// </summary>
    /// <param name="handler">The error handler instance.</param>
    /// <param name="context">The error context observed during mediation.</param>
    /// <param name="cancellationToken">The cancellation token for the invocation.</param>
    /// <returns>A task representing the asynchronous error handler operation.</returns>
    private static Task InvokeErrorHandlerAsync(
        IMessageErrorHandler handler,
        MessageErrorContext context,
        CancellationToken cancellationToken)
    {
        return PipelineHandlerInvocation.InvokeErrorHandlerAsync(handler, context, cancellationToken);
    }
}
