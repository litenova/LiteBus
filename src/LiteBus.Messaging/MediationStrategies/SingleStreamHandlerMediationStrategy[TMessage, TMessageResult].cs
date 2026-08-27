using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Mediates a message by invoking a single stream handler and yielding what it produces.
/// </summary>
/// <typeparam name="TMessage">Type of the message being handled.</typeparam>
/// <typeparam name="TMessageResult">Type of the items the handler streams.</typeparam>
/// <remarks>
///     <para>
///         This is the only mediation strategy that is an iterator, and that is what makes it different from the others
///         rather than merely longer. Nothing happens until the caller enumerates, faults surface from
///         <c>MoveNextAsync</c> rather than from a call, and the completion stage fires when the enumerator is disposed.
///         A caller who never enumerates produces no completion record at all.
///     </para>
///     <para>
///         Faults are routed to the error stage from four places: acquiring the handler's stream, acquiring an
///         enumerator, advancing one, and running post-handlers. All four go through the same local function, because
///         four copies of that block is how they drift apart.
///     </para>
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
    ///     The dependencies required for message handling, including handlers, pre-stage handlers, post-handlers, and
    ///     error handlers.
    /// </param>
    /// <param name="executionContext">
    ///     The context in which the mediation is executed, providing access to cancellation tokens, shared data, and
    ///     other execution-related information.
    /// </param>
    /// <returns>An asynchronous stream of results produced by the handler.</returns>
    /// <exception cref="NoHandlerFoundException">Thrown when no handler is found for the message type.</exception>
    public async IAsyncEnumerable<TMessageResult> Mediate(
        TMessage message,
        IMessageDependencies messageDependencies,
        IExecutionContext executionContext)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        IAsyncEnumerable<TMessageResult>? stream = null;
        var pipelineStopped = false;
        var startedAt = Stopwatch.GetTimestamp();
        var outcome = MediationOutcome.Succeeded;
        Exception? failure = null;
        string? reason = null;

        // Records a fault and offers it to the error stage. Every fault path in this method goes through here, and it
        // is a local function so it can record into the locals the enclosing iterator owns.
        async Task RouteFaultAsync(Exception fault, object? observedResult)
        {
            outcome = MediationOutcome.Failed;
            failure = fault;

            await messageDependencies
                .RunAsyncErrorHandlers(message, observedResult, fault, executionContext)
                .ConfigureAwait(false);
        }

        // Enumerates one stream to its end, routing any fault and stopping there. The handler's stream and a
        // post-handler's replacement are enumerated identically, so both come through here.
        async IAsyncEnumerable<TMessageResult> EnumerateAsync(IAsyncEnumerable<TMessageResult> source)
        {
            IAsyncEnumerator<TMessageResult>? enumerator = null;

            try
            {
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    enumerator = source.GetAsyncEnumerator(_cancellationToken);
                }
            }
            catch (Exception fault) when (MediationExceptionFilters.IsRecoverableMediationException(fault))
            {
                await RouteFaultAsync(fault, source).ConfigureAwait(false);
            }

            if (enumerator is null)
            {
                yield break;
            }

            try
            {
                while (true)
                {
                    TMessageResult? item;
                    bool hasItem;

                    using (AmbientExecutionContext.CreateScope(executionContext))
                    {
                        try
                        {
                            hasItem = await enumerator.MoveNextAsync().ConfigureAwait(false);
                            item = hasItem ? enumerator.Current : default;
                        }
                        catch (Exception fault) when (MediationExceptionFilters.IsRecoverableMediationException(fault))
                        {
                            await RouteFaultAsync(fault, source).ConfigureAwait(false);

                            // The enumerator is not valid after a fault, so stop rather than replaying the last item.
                            hasItem = false;
                            item = default;
                        }
                    }

                    if (!hasItem)
                    {
                        break;
                    }

                    yield return item!;
                }
            }
            finally
            {
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    await enumerator.DisposeAsync().ConfigureAwait(false);
                }
            }
        }

        try
        {
            try
            {
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    var stop = await messageDependencies
                        .RunAsyncPreStages(message, executionContext.CancellationToken)
                        .ConfigureAwait(false);

                    if (stop.StopsPipeline)
                    {
                        outcome = stop.Outcome;
                        reason = stop.Reason;
                        pipelineStopped = true;

                        if (stop.IsRefusal)
                        {
                            // A refusal carries no stream of its own, so the value comes from a registered mapper.
                            // Without one it reaches the caller as an exception.
                            try
                            {
                                stream = messageDependencies
                                    .ResolveRefusalResult<IAsyncEnumerable<TMessageResult>?>(message, stop);
                            }
                            catch (Exception refusal) when (refusal is LiteBusMessageDeniedException
                                                                or LiteBusMessageInvalidException)
                            {
                                failure = refusal;
                                throw;
                            }
                        }
                        else
                        {
                            // A typed shortcut always carries the stream it answers with, so this resolves it. Reaching
                            // here without one means the untyped shortcut contract was used on a stream query, which
                            // ResolveResult reports and analyzer LB1019 catches at compile time.
                            stream = stop.ResolveResult<IAsyncEnumerable<TMessageResult>?>(message.GetType());
                        }
                    }
                    else
                    {
                        var handler = SingleMainHandlerResolver.Resolve<TMessage>(messageDependencies).Handler.Value;

                        stream = HandlerInvocation.InvokeStreamHandler<TMessage, TMessageResult>(
                            handler,
                            message,
                            executionContext.CancellationToken);
                    }
                }
            }
            catch (Exception fault) when (MediationExceptionFilters.IsRecoverableMediationException(fault))
            {
                await RouteFaultAsync(fault, stream).ConfigureAwait(false);
            }

            if (pipelineStopped)
            {
                // A decision answered for the handler, so the caller gets whatever stream it supplied and the reactions
                // to work that never happened do not run.
                if (stream is not null)
                {
                    await foreach (var item in stream.WithCancellation(_cancellationToken).ConfigureAwait(false))
                    {
                        yield return item;
                    }
                }

                yield break;
            }

            await foreach (var item in EnumerateAsync(stream ?? Empty()).ConfigureAwait(false))
            {
                yield return item;
            }

            IAsyncEnumerable<TMessageResult>? overrideStream = null;

            try
            {
                using (AmbientExecutionContext.CreateScope(executionContext))
                {
                    await messageDependencies.RunAsyncPostHandlers(
                        message,
                        stream,
                        executionContext.CancellationToken).ConfigureAwait(false);

                    if (executionContext.MessageResult is IAsyncEnumerable<TMessageResult> replacement)
                    {
                        overrideStream = replacement;
                    }
                }
            }
            catch (Exception fault) when (MediationExceptionFilters.IsRecoverableMediationException(fault))
            {
                await RouteFaultAsync(fault, stream).ConfigureAwait(false);
            }

            if (overrideStream is null)
            {
                yield break;
            }

            await foreach (var item in EnumerateAsync(overrideStream).ConfigureAwait(false))
            {
                yield return item;
            }
        }
        finally
        {
            await messageDependencies
                .RunAsyncCompletionHandlers(
                    message,
                    executionContext,
                    outcome,
                    failure,
                    reason,
                    stream,
                    Stopwatch.GetElapsedTime(startedAt))
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Produces the stream a mediation enumerates when nothing supplied one.
    /// </summary>
    /// <returns>An empty asynchronous sequence.</returns>
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    // https://github.com/dotnet/runtime/issues/1128#issuecomment-571624647
    private static async IAsyncEnumerable<TMessageResult> Empty()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        yield break;
    }
}
