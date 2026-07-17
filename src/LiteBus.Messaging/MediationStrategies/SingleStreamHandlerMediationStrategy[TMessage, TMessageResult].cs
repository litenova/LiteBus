using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Mediates the handling of a message by invoking a single asynchronous stream handler.
///     This strategy ensures that only one handler processes the message and produces a stream of results.
/// </summary>
/// <typeparam name="TMessage">Type of the message being handled.</typeparam>
/// <typeparam name="TMessageResult">Type of the results returned by the message handler.</typeparam>
/// <remarks>
///     This strategy implements the streaming pattern for message handling, where a single handler
///     produces a stream of results that are yielded asynchronously. The strategy orchestrates the
///     execution of pre-handlers before the stream begins, processes each item in the stream,
///     and executes post-handlers after the stream completes.
///     Error handling is performed at multiple stages: during pre-handling, during stream enumeration,
///     and during post-handling. If a <see cref="LiteBusExecutionAbortedException" /> is caught at any stage,
///     the stream is terminated immediately.
/// </remarks>
public sealed class SingleStreamHandlerMediationStrategy<TMessage, TMessageResult> :
    IMessageMediationStrategy<TMessage, IAsyncEnumerable<TMessageResult>>
    where TMessage : notnull
{
    /// <summary>
    ///     The cancellation token passed to stream enumeration during mediation.
    /// </summary>
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SingleStreamHandlerMediationStrategy{TMessage,TMessageResult}" />
    ///     class.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that will be used for stream enumeration.</param>
    public SingleStreamHandlerMediationStrategy(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     Mediates a message by executing the appropriate stream handler and yielding results asynchronously.
    /// </summary>
    /// <param name="message">The message to be mediated.</param>
    /// <param name="messageDependencies">
    ///     The dependencies required for message handling, including handlers, pre-handlers,
    ///     post-handlers, and error handlers.
    /// </param>
    /// <param name="executionContext">
    ///     The context in which the mediation is executed, providing access to cancellation tokens,
    ///     shared data, and other execution-related information.
    /// </param>
    /// <returns>An asynchronous stream of results produced by the handler.</returns>
    /// <exception cref="NoHandlerFoundException">Thrown when no handler is found for the message type.</exception>
    /// <exception cref="MultipleHandlerFoundException">Thrown when more than one handler is found for the message type.</exception>
    /// <remarks>
    ///     The mediation process includes executing pre-handlers before starting the stream, obtaining the
    ///     stream from the handler, enumerating the stream and yielding each result, and executing post-handlers
    ///     after the stream completes. If an exception occurs during any stage, the appropriate error handlers are
    ///     executed. If a <see cref="LiteBusExecutionAbortedException" /> is caught, the stream is terminated immediately.
    /// </remarks>
    public async IAsyncEnumerable<TMessageResult> Mediate(
        TMessage message,
        IMessageDependencies messageDependencies,
        IExecutionContext executionContext)
    {
        IAsyncEnumerable<TMessageResult>? messageResultAsyncEnumerable = null;
        var shouldContinue = true;

        try
        {
            using (AmbientExecutionContext.CreateScope(executionContext))
            {
                await messageDependencies.RunAsyncPreHandlers(message, executionContext.CancellationToken).ConfigureAwait(false);

                var handler = SingleMainHandlerResolver.Resolve<TMessage>(messageDependencies).Handler.Value;
                messageResultAsyncEnumerable = HandlerInvocation.InvokeStreamHandler<TMessage, TMessageResult>(
                    handler,
                    message,
                    executionContext.CancellationToken);
            }
        }
        catch (LiteBusExecutionAbortedException)
        {
            shouldContinue = false;
        }
        catch (Exception exception) when (MediationExceptionFilters.IsRecoverableMediationException(exception))
        {
            using (AmbientExecutionContext.CreateScope(executionContext))
            {
                await messageDependencies.RunAsyncErrorHandlers(
                    message,
                    messageResultAsyncEnumerable,
                    ExceptionDispatchInfo.Capture(exception),
                    executionContext.CancellationToken).ConfigureAwait(false);
            }
        }

        if (!shouldContinue)
        {
            yield break;
        }

        messageResultAsyncEnumerable ??= Empty<TMessageResult>();

        var messageResultAsyncEnumerator = messageResultAsyncEnumerable.GetAsyncEnumerator(_cancellationToken);

        try
        {
            TMessageResult? item = default;
            var hasResult = true;

            while (hasResult && shouldContinue)
            {
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    try
                    {
                        hasResult = await messageResultAsyncEnumerator.MoveNextAsync().ConfigureAwait(false);
                        item = hasResult ? messageResultAsyncEnumerator.Current : default;
                    }
                    catch (LiteBusExecutionAbortedException)
                    {
                        shouldContinue = false;
                        continue;
                    }
                    catch (Exception exception) when (MediationExceptionFilters.IsRecoverableMediationException(exception))
                    {
                        await messageDependencies.RunAsyncErrorHandlers(
                            message,
                            messageResultAsyncEnumerable,
                            ExceptionDispatchInfo.Capture(exception),
                            executionContext.CancellationToken).ConfigureAwait(false);

                        // The source enumerator is no longer valid after a fault. Do not replay the previous item.
                        hasResult = false;
                        item = default;
                    }
                }

                if (item != null && hasResult && shouldContinue)
                {
                    yield return item;
                }
            }

            if (!shouldContinue)
            {
                yield break;
            }

            IAsyncEnumerable<TMessageResult>? overrideStream = null;

            try
            {
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    await messageDependencies.RunAsyncPostHandlers(
                        message,
                        messageResultAsyncEnumerable,
                        executionContext.CancellationToken).ConfigureAwait(false);

                    if (executionContext.MessageResult is IAsyncEnumerable<TMessageResult> stream)
                    {
                        overrideStream = stream;
                    }
                }
            }
            catch (LiteBusExecutionAbortedException)
            {
                // Stream items were already yielded; post-handler abort is ignored.
            }
            catch (Exception exception) when (MediationExceptionFilters.IsRecoverableMediationException(exception))
            {
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    await messageDependencies.RunAsyncErrorHandlers(
                        message,
                        messageResultAsyncEnumerable,
                        ExceptionDispatchInfo.Capture(exception),
                        executionContext.CancellationToken).ConfigureAwait(false);
                }
            }

            if (overrideStream is not null)
            {
                var overrideEnumerator = overrideStream.GetAsyncEnumerator(_cancellationToken);

                try
                {
                    while (await overrideEnumerator.MoveNextAsync().ConfigureAwait(false))
                    {
                        yield return overrideEnumerator.Current;
                    }
                }
                finally
                {
                    await overrideEnumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await messageResultAsyncEnumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates an empty asynchronous enumerable.
    /// </summary>
    /// <typeparam name="T">The type of elements in the enumerable.</typeparam>
    /// <returns>An empty asynchronous enumerable.</returns>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

    // https://github.com/dotnet/runtime/issues/1128#issuecomment-571624647
    private static async IAsyncEnumerable<T> Empty<T>()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        yield break;
    }
}
