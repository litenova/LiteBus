using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
///     Invokes pipeline handlers with an explicit cancellation token instead of relying on ambient context timing.
/// </summary>
internal static class PipelineHandlerInvocation
{
    /// <summary>
    ///     Caches discovered asynchronous pipeline methods per handler runtime type.
    /// </summary>
    private static readonly ConcurrentDictionary<(Type HandlerType, string MethodName), MethodInfo?> AsyncPipelineMethods = new();

    /// <summary>
    ///     Invokes a pre-handler asynchronously with the supplied cancellation token.
    /// </summary>
    /// <param name="handler">The pre-handler instance.</param>
    /// <param name="message">The message to pass to the pre-handler.</param>
    /// <param name="cancellationToken">The cancellation token for the pre-handler invocation.</param>
    /// <returns>A task representing the asynchronous pre-handler operation.</returns>
    public static Task InvokePreHandlerAsync(
        IMessagePreHandler handler,
        object message,
        CancellationToken cancellationToken)
    {
        return InvokeAsyncPipelineMethod(
            handler,
            "PreHandleAsync",
            message,
            () => handler.PreHandle(message),
            [],
            cancellationToken);
    }

    /// <summary>
    ///     Invokes a post-handler asynchronously with the supplied cancellation token.
    /// </summary>
    /// <param name="handler">The post-handler instance.</param>
    /// <param name="message">The handled message.</param>
    /// <param name="messageResult">The result produced by the main handler, if any.</param>
    /// <param name="cancellationToken">The cancellation token for the post-handler invocation.</param>
    /// <returns>A task representing the asynchronous post-handler operation.</returns>
    public static Task InvokePostHandlerAsync(
        IMessagePostHandler handler,
        object message,
        object? messageResult,
        CancellationToken cancellationToken)
    {
        return InvokeAsyncPipelineMethod(
            handler,
            "PostHandleAsync",
            message,
            () => handler.PostHandle(message, messageResult),
            [messageResult],
            cancellationToken);
    }

    /// <summary>
    ///     Invokes an error handler asynchronously with the supplied cancellation token.
    /// </summary>
    /// <param name="handler">The error handler instance.</param>
    /// <param name="context">The error context observed during mediation.</param>
    /// <param name="cancellationToken">The cancellation token for the error handler invocation.</param>
    /// <returns>A task representing the asynchronous error handler operation.</returns>
    public static Task InvokeErrorHandlerAsync(
        IMessageErrorHandler handler,
        MessageErrorContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(context);
        _ = cancellationToken;

        var outcome = handler.HandleError(context);

        return outcome as Task ?? Task.CompletedTask;
    }

    /// <summary>
    ///     Invokes an asynchronous pipeline method discovered on a handler interface when present.
    /// </summary>
    /// <param name="handler">The handler instance.</param>
    /// <param name="asyncMethodName">The asynchronous method name to invoke when available.</param>
    /// <param name="message">The message argument for the handler method.</param>
    /// <param name="fallback">The fallback invocation used when no asynchronous method is found.</param>
    /// <param name="additionalArguments">Additional arguments appended before the cancellation token.</param>
    /// <param name="cancellationToken">The cancellation token passed to the asynchronous method.</param>
    /// <returns>A task representing the handler invocation.</returns>
    private static Task InvokeAsyncPipelineMethod(
        object handler,
        string asyncMethodName,
        object message,
        Func<object> fallback,
        object?[] additionalArguments,
        CancellationToken cancellationToken)
    {
        var method = AsyncPipelineMethods.GetOrAdd(
            (handler.GetType(), asyncMethodName),
            key => FindAsyncPipelineMethod(key.HandlerType, key.MethodName));

        if (method is not null)
        {
            object?[] arguments = [message, ..additionalArguments, cancellationToken];

            try
            {
                return (Task) method.Invoke(handler, arguments)!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        return (Task) fallback();
    }

    /// <summary>
    ///     Finds the first declared asynchronous pipeline method on a handler type.
    /// </summary>
    /// <param name="handlerType">The concrete handler runtime type.</param>
    /// <param name="asyncMethodName">The asynchronous method name to locate.</param>
    /// <returns>The matching method when found; otherwise, <see langword="null" />.</returns>
    private static MethodInfo? FindAsyncPipelineMethod(Type handlerType, string asyncMethodName)
    {
        foreach (var handlerInterface in handlerType.GetInterfaces())
        {
            if (!handlerInterface.IsGenericType)
            {
                continue;
            }

            var method = handlerInterface.GetMethod(
                asyncMethodName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (method is not null && method.ReturnType == typeof(Task))
            {
                return method;
            }
        }

        return null;
    }
}
