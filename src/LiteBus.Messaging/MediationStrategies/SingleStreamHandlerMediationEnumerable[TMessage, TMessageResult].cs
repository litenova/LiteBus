using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Pipeline;

namespace LiteBus.Messaging.MediationStrategies;

/// <summary>
///     Defers stream mediation until enumeration and links the enumerator token with the mediation token.
/// </summary>
/// <typeparam name="TMessage">Type of the message being handled.</typeparam>
/// <typeparam name="TMessageResult">Type of the items the handler streams.</typeparam>
/// <remarks>
///     Returned by <see cref="SingleStreamHandlerMediationStrategy{TMessage, TMessageResult}" /> so nothing runs
///     until the caller enumerates. Each <see cref="GetAsyncEnumerator" /> call links the caller supplied
///     enumerator token with the token captured at mediation time and runs the pipeline with the linked token.
/// </remarks>
internal sealed class SingleStreamHandlerMediationEnumerable<TMessage, TMessageResult> : IAsyncEnumerable<TMessageResult>
    where TMessage : notnull
{
    /// <summary>
    ///     The message to be mediated.
    /// </summary>
    private readonly TMessage _message;

    /// <summary>
    ///     The dependencies required for message handling.
    /// </summary>
    private readonly IMessageDependencies _messageDependencies;

    /// <summary>
    ///     The context in which the mediation is executed.
    /// </summary>
    private readonly IExecutionContext _executionContext;

    /// <summary>
    ///     The cancellation token captured at mediation time.
    /// </summary>
    private readonly CancellationToken _cancellationToken;

    /// <summary>
    ///     Initializes a new instance of the <see cref="SingleStreamHandlerMediationEnumerable{TMessage, TMessageResult}" />
    ///     class.
    /// </summary>
    /// <param name="message">The message to be mediated.</param>
    /// <param name="messageDependencies">The dependencies required for message handling.</param>
    /// <param name="executionContext">The context in which the mediation is executed.</param>
    /// <param name="cancellationToken">The cancellation token captured at mediation time.</param>
    public SingleStreamHandlerMediationEnumerable(
        TMessage message,
        IMessageDependencies messageDependencies,
        IExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(messageDependencies);
        ArgumentNullException.ThrowIfNull(executionContext);

        _message = message;
        _messageDependencies = messageDependencies;
        _executionContext = executionContext;
        _cancellationToken = cancellationToken;
    }

    /// <summary>
    ///     Returns an enumerator that runs the mediation with the enumerator and mediation tokens linked.
    /// </summary>
    /// <param name="enumeratorToken">The token supplied by the caller enumerating the stream.</param>
    /// <returns>An enumerator over the mediated stream.</returns>
    public IAsyncEnumerator<TMessageResult> GetAsyncEnumerator(CancellationToken enumeratorToken = default)
    {
        var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(_cancellationToken, enumeratorToken);
        var linkedToken = linkedSource.Token;

        try
        {
            return MediateAsync(_message, _messageDependencies, _executionContext, linkedSource, linkedToken)
                .GetAsyncEnumerator(linkedToken);
        }
        catch
        {
            linkedSource.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Mediates the message with the linked cancellation token and disposes the linked source when done.
    /// </summary>
    /// <param name="message">The message to be mediated.</param>
    /// <param name="messageDependencies">The dependencies required for message handling.</param>
    /// <param name="executionContext">The context in which the mediation is executed.</param>
    /// <param name="linkedSource">The linked source disposed when enumeration ends.</param>
    /// <param name="linkedToken">The linked cancellation token for stream enumeration.</param>
    /// <returns>An asynchronous stream of results produced by the handler.</returns>
    /// <exception cref="NoHandlerFoundException">Thrown when no handler is found for the message type.</exception>
    private static async IAsyncEnumerable<TMessageResult> MediateAsync(
        TMessage message,
        IMessageDependencies messageDependencies,
        IExecutionContext executionContext,
        CancellationTokenSource linkedSource,
        [EnumeratorCancellation] CancellationToken linkedToken = default)
    {
        try
        {
            IAsyncEnumerable<TMessageResult>? stream = null;
            var pipelineStopped = false;
            var startedAt = Stopwatch.GetTimestamp();
            using var activity = MediationTelemetry.StartMediation(message.GetType());
            var outcome = MediationOutcome.Succeeded;
            Exception? failure = null;
            string? reason = null;
            string? code = null;

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
                        enumerator = source.GetAsyncEnumerator(linkedToken);
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
                                linkedToken.ThrowIfCancellationRequested();
                                hasItem = await enumerator.MoveNextAsync().ConfigureAwait(false);
                                item = hasItem ? enumerator.Current : default;
                            }
                            catch (Exception fault) when (MediationExceptionFilters.IsRecoverableMediationException(fault))
                            {
                                await RouteFaultAsync(fault, source).ConfigureAwait(false);

                                // The enumerator is not valid after a fault, so decision rather than replaying the last item.
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
                        var decision = await messageDependencies
                            .RunAsyncPreStages(message, executionContext.CancellationToken)
                            .ConfigureAwait(false);

                        if (decision.StopsPipeline)
                        {
                            outcome = decision.Outcome;
                            reason = decision.Reason;
                            code = decision.Code;

                            // Only a Try call installs a capture, so this is a dictionary miss on every ordinary mediation.
                            MediationEndingCapture.Record(executionContext, decision);
                            pipelineStopped = true;

                            if (decision.IsRefusal)
                            {
                                // A refusal carries no stream of its own, so the value comes from a registered mapper.
                                // Without one it reaches the caller as an exception.
                                try
                                {
                                    stream = messageDependencies
                                        .ResolveRefusalResult<IAsyncEnumerable<TMessageResult>?>(message, decision);
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
                                stream = decision.ResolveResult<IAsyncEnumerable<TMessageResult>?>(message.GetType());
                            }
                        }
                        else
                        {
                            var handler = SingleMainHandlerResolver.Resolve<TMessage>(messageDependencies).Handler.Value;

                            stream = HandlerInvocation.InvokeStreamHandler<TMessage, TMessageResult>(
                                handler,
                                message,
                                linkedToken);
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
                        await foreach (var item in stream.WithCancellation(linkedToken).ConfigureAwait(false))
                        {
                            yield return item;
                        }
                    }

                    yield break;
                }

                await foreach (var item in EnumerateAsync(stream ?? Empty()).WithCancellation(linkedToken).ConfigureAwait(false))
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

                await foreach (var item in EnumerateAsync(overrideStream).WithCancellation(linkedToken).ConfigureAwait(false))
                {
                    yield return item;
                }
            }
            finally
            {
                var elapsed = Stopwatch.GetElapsedTime(startedAt);
                MediationTelemetry.RecordMediation(activity, message.GetType(), outcome, code, elapsed);

                await messageDependencies
                    .RunAsyncCompletionHandlers(
                        message,
                        executionContext,
                        outcome,
                        failure,
                        reason,
                        code,
                        stream,
                        elapsed)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            linkedSource.Dispose();
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
