using System;
using System.Collections.Generic;
using System.Diagnostics;
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
///     and during post-handling. When a gate stops the pipeline, the main handler never runs and the stream yields
///     whatever the directive supplied, or nothing.
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
    ///     executed. When a gate stops the pipeline, the mediation reports <see cref="MessageOutcome.ShortCircuited" />
    ///     or <see cref="MessageOutcome.Denied" />.
    /// </remarks>
    public async IAsyncEnumerable<TMessageResult> Mediate(
        TMessage message,
        IMessageDependencies messageDependencies,
        IExecutionContext executionContext)
    {
        IAsyncEnumerable<TMessageResult>? messageResultAsyncEnumerable = null;
        var shouldContinue = true;
        var pipelineStopped = false;
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = MessageOutcome.Succeeded;
        Exception? failure = null;
        string? reason = null;

        try
        {
            try
            {
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    var directive = await messageDependencies
                        .RunAsyncPreHandlers(message, executionContext.CancellationToken)
                        .ConfigureAwait(false);

                    if (directive.StopsPipeline)
                    {
                        outcome = directive.ToOutcome();
                        reason = directive.Reason;
                        shouldContinue = false;
                        pipelineStopped = true;

                        if (directive.IsUnansweredDenial())
                        {
                            var denial = directive.CreateDenial(message.GetType());
                            failure = denial;
                            throw denial;
                        }

                        // A stopping directive over a stream supplies a replacement stream. Supplying none is a
                        // legitimate answer for a stream, and means the caller enumerates nothing.
                        messageResultAsyncEnumerable = directive.HasResult
                            ? directive.ResolveResult<IAsyncEnumerable<TMessageResult>?>(message.GetType())
                            : null;
                    }
                    else
                    {
                        var handler = SingleMainHandlerResolver.Resolve<TMessage>(messageDependencies).Handler.Value;
                        messageResultAsyncEnumerable = HandlerInvocation.InvokeStreamHandler<TMessage, TMessageResult>(
                            handler,
                            message,
                            executionContext.CancellationToken);
                    }
                }
            }
            catch (Exception exception) when (MediationExceptionFilters.IsRecoverableMediationException(exception))
            {
                outcome = MessageOutcome.Failed;
                failure = exception;
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    await messageDependencies.RunAsyncErrorHandlers(
                        message,
                        messageResultAsyncEnumerable,
                        ExceptionDispatchInfo.Capture(exception),
                        executionContext.CancellationToken).ConfigureAwait(false);
                }
            }

            if (pipelineStopped)
            {
                // The gate answered for the handler, so the caller gets whatever stream it supplied and the reactions to
                // work that never happened do not run. Supplying no stream is a legitimate answer and yields nothing.
                if (messageResultAsyncEnumerable is not null)
                {
                    await foreach (var item in messageResultAsyncEnumerable
                                       .WithCancellation(_cancellationToken)
                                       .ConfigureAwait(false))
                    {
                        yield return item;
                    }
                }

                yield break;
            }

            messageResultAsyncEnumerable ??= Empty<TMessageResult>();

            IAsyncEnumerator<TMessageResult>? messageResultAsyncEnumerator = null;

            try
            {
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    messageResultAsyncEnumerator = messageResultAsyncEnumerable.GetAsyncEnumerator(_cancellationToken);
                }
            }
            catch (Exception exception) when (MediationExceptionFilters.IsRecoverableMediationException(exception))
            {
                outcome = MessageOutcome.Failed;
                failure = exception;
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    await messageDependencies.RunAsyncErrorHandlers(
                        message,
                        messageResultAsyncEnumerable,
                        ExceptionDispatchInfo.Capture(exception),
                        executionContext.CancellationToken).ConfigureAwait(false);
                }
            }

            messageResultAsyncEnumerator ??= Empty<TMessageResult>().GetAsyncEnumerator(_cancellationToken);

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
                                    catch (Exception exception) when (MediationExceptionFilters.IsRecoverableMediationException(exception))
                        {
                            outcome = MessageOutcome.Failed;
                            failure = exception;
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

                    if (hasResult && shouldContinue)
                    {
                        yield return item!;
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
                catch (Exception exception) when (MediationExceptionFilters.IsRecoverableMediationException(exception))
                {
                    outcome = MessageOutcome.Failed;
                    failure = exception;
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
                    IAsyncEnumerator<TMessageResult>? overrideEnumerator = null;

                    try
                    {
                        using (AmbientExecutionContext.CreateScope(executionContext))
                        {
                            overrideEnumerator = overrideStream.GetAsyncEnumerator(_cancellationToken);
                        }
                    }
                    catch (Exception exception) when (MediationExceptionFilters.IsRecoverableMediationException(exception))
                    {
                        outcome = MessageOutcome.Failed;
                        failure = exception;
                        using (AmbientExecutionContext.CreateScope(executionContext))
                        {
                            await messageDependencies.RunAsyncErrorHandlers(
                                message,
                                overrideStream,
                                ExceptionDispatchInfo.Capture(exception),
                                executionContext.CancellationToken).ConfigureAwait(false);
                        }
                    }

                    if (overrideEnumerator is not null)
                    {
                        try
                        {
                            var hasOverrideResult = true;

                            while (hasOverrideResult)
                            {
                                TMessageResult? overrideItem = default;

                                using (AmbientExecutionContext.CreateScope(executionContext))
                                {
                                    try
                                    {
                                        hasOverrideResult = await overrideEnumerator.MoveNextAsync().ConfigureAwait(false);
                                        overrideItem = hasOverrideResult ? overrideEnumerator.Current : default;
                                    }
                                                            catch (Exception exception) when (MediationExceptionFilters.IsRecoverableMediationException(exception))
                                    {
                                        outcome = MessageOutcome.Failed;
                                        failure = exception;
                                        await messageDependencies.RunAsyncErrorHandlers(
                                            message,
                                            overrideStream,
                                            ExceptionDispatchInfo.Capture(exception),
                                            executionContext.CancellationToken).ConfigureAwait(false);

                                        hasOverrideResult = false;
                                    }
                                }

                                if (hasOverrideResult)
                                {
                                    yield return overrideItem!;
                                }
                            }
                        }
                        finally
                        {
                            using (AmbientExecutionContext.CreateScope(executionContext))
                            {
                                await overrideEnumerator.DisposeAsync().ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
            finally
            {
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    await messageResultAsyncEnumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
        }
        finally
        {
            await messageDependencies.RunAsyncCompletionHandlers(
                    message,
                    executionContext,
                    outcome,
                    messageResultAsyncEnumerable,
                    failure,
                    reason,
                    Stopwatch.GetElapsedTime(startedAt))
                .ConfigureAwait(false);
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
