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
    /// <returns>
    ///     The directive from the first pre-handler that short-circuited, or <see cref="PipelineDirective.Continue" />
    ///     when every pre-handler let the pipeline proceed. Pre-handlers after a short-circuit do not run.
    /// </returns>
    public static async Task<PipelineDirective> RunAsyncPreHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        CancellationToken cancellationToken)
    {
        foreach (var preHandler in messageDependencies.IndirectPreHandlers)
        {
            var directive = await preHandler.Handler.Value.PreHandleAsync(message, cancellationToken).ConfigureAwait(false);

            if (directive.IsShortCircuit)
            {
                return directive;
            }
        }

        foreach (var preHandler in messageDependencies.PreHandlers)
        {
            var directive = await preHandler.Handler.Value.PreHandleAsync(message, cancellationToken).ConfigureAwait(false);

            if (directive.IsShortCircuit)
            {
                return directive;
            }
        }

        return PipelineDirective.Continue;
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
        var executionContext = AmbientExecutionContext.GetCurrentOrDefault();

        foreach (var postHandler in messageDependencies.PostHandlers)
        {
            if (executionContext?.PostHandlersSuppressed == true)
            {
                return;
            }

            await InvokePostHandlerAsync(postHandler.Handler.Value, message, messageResult, cancellationToken).ConfigureAwait(false);
        }

        foreach (var postHandler in messageDependencies.IndirectPostHandlers)
        {
            if (executionContext?.PostHandlersSuppressed == true)
            {
                return;
            }

            await InvokePostHandlerAsync(postHandler.Handler.Value, message, messageResult, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Runs completion handlers for a mediation that has ended, on every outcome path.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating completion handlers.</param>
    /// <param name="context">The completion context describing how the mediation ended.</param>
    /// <param name="cancellationToken">The cancellation token passed to each completion handler invocation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    /// <remarks>
    ///     <para>
    ///         Direct handlers run before indirect handlers, matching post-handler ordering, so that a globally registered
    ///         observer sees the message last.
    ///     </para>
    ///     <para>
    ///         When the mediation already ended in a fault, an exception raised by a completion handler is suppressed so
    ///         that an observer bug cannot replace the original fault. When the mediation succeeded, the exception
    ///         propagates.
    ///     </para>
    /// </remarks>
    public static async Task RunAsyncCompletionHandlers(
        this IMessageDependencies messageDependencies,
        MessageCompletionContext context,
        CancellationToken cancellationToken)
    {
        if (messageDependencies.CompletionHandlers.Count + messageDependencies.IndirectCompletionHandlers.Count == 0)
        {
            return;
        }

        var suppressFailures = context.Outcome != MessageOutcome.Succeeded;

        foreach (var completionHandler in messageDependencies.CompletionHandlers)
        {
            await InvokeCompletionHandlerAsync(completionHandler.Handler.Value, context, suppressFailures, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var completionHandler in messageDependencies.IndirectCompletionHandlers)
        {
            await InvokeCompletionHandlerAsync(completionHandler.Handler.Value, context, suppressFailures, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Builds a completion context and runs completion handlers inside the ambient execution scope.
    /// </summary>
    /// <param name="messageDependencies">The message dependencies encapsulating completion handlers.</param>
    /// <param name="message">The message that was mediated.</param>
    /// <param name="executionContext">The execution context used to scope the completion handlers.</param>
    /// <param name="outcome">The outcome describing how the mediation ended.</param>
    /// <param name="messageResult">The result observed before the mediation ended, when any.</param>
    /// <param name="exception">The exception that ended the mediation, when any.</param>
    /// <param name="abortReason">The reason the execution was aborted, when any.</param>
    /// <param name="duration">The elapsed mediation time.</param>
    /// <returns>A task representing the asynchronous completion stage.</returns>
    public static async Task RunAsyncCompletionHandlers(
        this IMessageDependencies messageDependencies,
        object message,
        IExecutionContext executionContext,
        MessageOutcome outcome,
        object? messageResult,
        Exception? exception,
        string? abortReason,
        TimeSpan duration)
    {
        if (messageDependencies.CompletionHandlers.Count + messageDependencies.IndirectCompletionHandlers.Count == 0)
        {
            return;
        }

        var context = new MessageCompletionContext
        {
            Message = message,
            Outcome = outcome,
            MessageResult = messageResult,
            Exception = exception,
            AbortReason = abortReason,
            Duration = duration
        };

        using (AmbientExecutionContext.CreateScope(executionContext))
        {
            await messageDependencies.RunAsyncCompletionHandlers(context, executionContext.CancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Invokes a completion handler, optionally suppressing its failure when the pipeline already faulted.
    /// </summary>
    /// <param name="handler">The completion handler instance.</param>
    /// <param name="context">The completion context observed at the end of mediation.</param>
    /// <param name="suppressFailures">Whether an exception raised by the handler is swallowed.</param>
    /// <param name="cancellationToken">The cancellation token for the invocation.</param>
    /// <returns>A task representing the asynchronous completion handler operation.</returns>
    private static async Task InvokeCompletionHandlerAsync(
        IMessageCompletionHandler handler,
        MessageCompletionContext context,
        bool suppressFailures,
        CancellationToken cancellationToken)
    {
        if (!suppressFailures)
        {
            await PipelineHandlerInvocation.InvokeCompletionHandlerAsync(handler, context, cancellationToken)
                .ConfigureAwait(false);

            return;
        }

        try
        {
            await PipelineHandlerInvocation.InvokeCompletionHandlerAsync(handler, context, cancellationToken)
                .ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A completion handler observes the outcome and must never replace the original fault.
        catch (Exception)
#pragma warning restore CA1031
        {
            // The mediation already ended in a fault, so the observer's failure is intentionally swallowed.
        }
    }

    /// <summary>
    ///     Invokes a post-handler using an explicit cancellation token when an asynchronous method is available.
    /// </summary>
    /// <param name="handler">The post-handler instance.</param>
    /// <param name="message">The handled message.</param>
    /// <param name="messageResult">The result produced by the main handler, if any.</param>
    /// <param name="cancellationToken">The cancellation token for the invocation.</param>
    /// <returns>A task representing the asynchronous post-handler operation.</returns>
    private static Task InvokePostHandlerAsync(
        IMessagePostHandler handler,
        object message,
        object? messageResult,
        CancellationToken cancellationToken)
    {
        return PipelineHandlerInvocation.InvokePostHandlerAsync(handler, message, messageResult, cancellationToken);
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
