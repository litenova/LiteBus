#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace LiteBus.Messaging.Abstractions;

/// <summary>
/// Mediates the handling of a message by invoking a single asynchronous stream handler.
/// This strategy ensures that only one handler processes the message and produces a stream of results.
/// </summary>
/// <typeparam name="TMessage">Type of the message being handled.</typeparam>
/// <typeparam name="TMessageResult">Type of the results returned by the message handler.</typeparam>
/// <remarks>
/// This strategy implements the streaming pattern for message handling, where a single handler
/// produces a stream of results that are yielded asynchronously. The strategy orchestrates the
/// execution of pre-handlers before the stream begins, processes each item in the stream,
/// and executes post-handlers after the stream completes.
/// 
/// Error handling is performed at multiple stages: during pre-handling, during stream enumeration,
/// and during post-handling. If a <see cref="LiteBusExecutionAbortedException"/> is caught at any stage,
/// the stream is terminated immediately.
/// </remarks>
public sealed class SingleStreamHandlerMediationStrategy<TMessage, TMessageResult> : IMessageMediationStrategy<TMessage, IAsyncEnumerable<TMessageResult>>
    where TMessage : notnull
{
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    /// Initializes a new instance of the <see cref="SingleStreamHandlerMediationStrategy{TMessage, TMessageResult}"/> class.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token that will be used for stream enumeration.</param>
    public SingleStreamHandlerMediationStrategy(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    /// Mediates a message by executing the appropriate stream handler and yielding results asynchronously.
    /// </summary>
    /// <param name="message">The message to be mediated.</param>
    /// <param name="messageDependencies">The dependencies required for message handling, including handlers, pre-handlers, post-handlers, and error handlers.</param>
    /// <param name="executionContext">The context in which the mediation is executed, providing access to cancellation tokens, shared data, and other execution-related information.</param>
    /// <returns>An asynchronous stream of results produced by the handler.</returns>
    /// <exception cref="MultipleHandlerFoundException">Thrown when more than one handler is found for the message type.</exception>
    /// <remarks>
    /// The mediation process includes:
    /// 1. Executing pre-handlers before starting the stream.
    /// 2. Obtaining the stream from the handler.
    /// 3. Enumerating the stream and yielding each result.
    /// 4. Executing post-handlers after the stream completes.
    /// 
    /// If an exception occurs during any stage, the appropriate error handlers are executed.
    /// If a <see cref="LiteBusExecutionAbortedException"/> is caught, the stream is terminated immediately.
    /// </remarks>
    public async IAsyncEnumerable<TMessageResult> Mediate(TMessage message, IMessageDependencies messageDependencies, IExecutionContext executionContext)
    {
        if (messageDependencies.Handlers.Count > 1)
        {
            throw new MultipleHandlerFoundException(typeof(TMessage), messageDependencies.Handlers.Count);
        }

        IAsyncEnumerable<TMessageResult>? messageResultAsyncEnumerable = null;
        bool shouldContinue = true;

        try
        {
            AmbientExecutionContext.Current = executionContext;

            await messageDependencies.RunAsyncPreHandlers(message);

            var handler = messageDependencies.Handlers.Single().Handler.Value;

            messageResultAsyncEnumerable = (IAsyncEnumerable<TMessageResult>) handler!.Handle(message);
        }
        catch (LiteBusExecutionAbortedException)
        {
            // Execution was aborted during pre-handling, terminate the stream
            shouldContinue = false;
        }
        catch (Exception exception) when (exception is not LiteBusExecutionAbortedException)
        {
            await messageDependencies.RunAsyncErrorHandlers(message, messageResultAsyncEnumerable, ExceptionDispatchInfo.Capture(exception));
        }

        if (!shouldContinue)
        {
            // Early termination, no items to yield
            yield break;
        }

        messageResultAsyncEnumerable ??= Empty<TMessageResult>();

        await using var messageResultAsyncEnumerator = messageResultAsyncEnumerable.GetAsyncEnumerator(_cancellationToken);

        TMessageResult? item = default;
        var hasResult = true;

        while (hasResult && shouldContinue)
        {
            try
            {
                hasResult = await messageResultAsyncEnumerator.MoveNextAsync().ConfigureAwait(false);

                item = hasResult ? messageResultAsyncEnumerator.Current : default;
            }
            catch (LiteBusExecutionAbortedException)
            {
                // Execution was aborted during stream enumeration, terminate the stream
                shouldContinue = false;
                continue;
            }
            catch (Exception exception) when (exception is not LiteBusExecutionAbortedException)
            {
                await messageDependencies.RunAsyncErrorHandlers(message, messageResultAsyncEnumerable, ExceptionDispatchInfo.Capture(exception));
            }

            if (item != null && hasResult && shouldContinue)
            {
                AmbientExecutionContext.Current = executionContext;
                yield return item;
            }
        }

        if (!shouldContinue)
        {
            // Stream was terminated early, skip post-handlers
            yield break;
        }

        try
        {
            AmbientExecutionContext.Current = executionContext;
            await messageDependencies.RunAsyncPostHandlers(message, messageResultAsyncEnumerable);
        }
        catch (LiteBusExecutionAbortedException)
        {
            // Execution was aborted during post-handling, but we've already yielded all items
            // No action needed
        }
        catch (Exception exception) when (exception is not LiteBusExecutionAbortedException)
        {
            AmbientExecutionContext.Current = executionContext;
            await messageDependencies.RunAsyncErrorHandlers(message, messageResultAsyncEnumerable, ExceptionDispatchInfo.Capture(exception));
        }
    }

    /// <summary>
    /// Creates an empty asynchronous enumerable.
    /// </summary>
    /// <typeparam name="T">The type of elements in the enumerable.</typeparam>
    /// <returns>An empty asynchronous enumerable.</returns>
    /// <remarks>
    /// This method is used to provide an empty stream when the handler fails to produce a stream.
    /// The pragma directives suppress compiler warnings about the async method not containing await operators.
    /// </remarks>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously

    // https://github.com/dotnet/runtime/issues/1128#issuecomment-571624647
    private static async IAsyncEnumerable<T> Empty<T>()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        yield break;
    }
}