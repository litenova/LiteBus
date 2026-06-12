using System;
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
        return InvokeAsyncPipelineMethod(handler, "PreHandleAsync", message, cancellationToken, () => handler.PreHandle(message));
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
            cancellationToken,
            () => handler.PostHandle(message, messageResult),
            messageResult);
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
        _ = cancellationToken;
        return (Task) handler.HandleError(context);
    }

    /// <summary>
    ///     Invokes an asynchronous pipeline method discovered on a handler interface when present.
    /// </summary>
    /// <param name="handler">The handler instance.</param>
    /// <param name="asyncMethodName">The asynchronous method name to invoke when available.</param>
    /// <param name="message">The message argument for the handler method.</param>
    /// <param name="cancellationToken">The cancellation token passed to the asynchronous method.</param>
    /// <param name="fallback">The fallback invocation used when no asynchronous method is found.</param>
    /// <param name="additionalArguments">Additional arguments appended before the cancellation token.</param>
    /// <returns>A task representing the handler invocation.</returns>
    private static Task InvokeAsyncPipelineMethod(
        object handler,
        string asyncMethodName,
        object message,
        CancellationToken cancellationToken,
        Func<object> fallback,
        params object?[] additionalArguments)
    {
        foreach (var handlerInterface in handler.GetType().GetInterfaces())
        {
            if (!handlerInterface.IsGenericType)
            {
                continue;
            }

            var method = handlerInterface.GetMethod(
                asyncMethodName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

            if (method is null || method.ReturnType != typeof(Task))
            {
                continue;
            }

            var arguments = new List<object?> { message };
            arguments.AddRange(additionalArguments);
            arguments.Add(cancellationToken);

            try
            {
                return (Task) method.Invoke(handler, arguments.ToArray())!;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        return (Task) fallback();
    }
}
