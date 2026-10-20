using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Messaging.Processing;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Outbox;

/// <summary>
///     Shared dispatch and terminal transition logic for outbox processor implementations.
/// </summary>
internal static class OutboxProcessorEnvelopeHandler
{
    /// <summary>
    ///     Publishes one leased message envelope and records its terminal state for this attempt.
    /// </summary>
    /// <param name="envelope">The leased outbox message returned by the store.</param>
    /// <param name="dispatcher">The dispatcher used to publish the message.</param>
    /// <param name="options">The retry settings applied after publication failures.</param>
    /// <param name="clock">The time provider used for retry visibility timestamps.</param>
    /// <param name="accumulator">The pass accumulator that collects post-transition envelopes.</param>
    /// <param name="logger">The logger used for publication failure diagnostics.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>
    ///     The post-transition envelope when dispatch finished with a terminal outcome for this attempt; otherwise
    ///     <see langword="null" /> when dispatch was canceled.
    /// </returns>
    public static async Task<OutboxEnvelope?> ProcessAsync(
        OutboxEnvelope envelope,
        IOutboxDispatcher dispatcher,
        OutboxProcessorOptions options,
        TimeProvider clock,
        IProcessorPassRecorder<OutboxEnvelope> accumulator,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var updated = await DispatchAsync(envelope, dispatcher, options, clock, logger, [], cancellationToken)
            .ConfigureAwait(false);

        if (updated is null)
        {
            return null;
        }

        RecordTerminalOutcome(updated, accumulator);
        return updated;
    }

    /// <summary>
    ///     Executes publication and maps failures to terminal retry or dead-letter transitions.
    /// </summary>
    /// <param name="envelope">The leased outbox message returned by the store.</param>
    /// <param name="dispatcher">The dispatcher used to publish the message.</param>
    /// <param name="options">The retry settings applied after publication failures.</param>
    /// <param name="clock">The time provider used for retry visibility timestamps.</param>
    /// <param name="logger">The logger used for publication failure diagnostics.</param>
    /// <param name="hooks">The orchestration processor envelope hooks invoked before dispatch.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>
    ///     The post-transition envelope when dispatch finished with a terminal outcome for this attempt; otherwise
    ///     <see langword="null" /> when dispatch was canceled.
    /// </returns>
    internal static async Task<OutboxEnvelope?> DispatchAsync(
        OutboxEnvelope envelope,
        IOutboxDispatcher dispatcher,
        OutboxProcessorOptions options,
        TimeProvider clock,
        ILogger logger,
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        CancellationToken cancellationToken)
    {
        MessageProcessorDiagnostics.TryGetParentActivityContext(envelope.TraceContext, out var parentContext);

        using var messageActivity = OutboxProcessorTelemetry.ActivitySource.StartActivity(
            "outbox.processor.message",
            ActivityKind.Internal,
            parentContext);

        messageActivity?.SetTag("litebus.message_id", envelope.Id);
        var dispatchCompleted = false;

        // One adapter serves every hook phase of this dispatch. Each phase used to build its own, so a single
        // envelope allocated five of an object whose only job is to project a record behind an interface.
        var hookEnvelope = new OutboxProcessorEnvelopeAdapter(envelope);

        try
        {
            await ProcessorHookRunner.RunBeforeDispatchAsync(hooks, hookEnvelope, cancellationToken)
                .ConfigureAwait(false);

            ProcessorHookRunner.RunPrepareDispatchScope(hooks, hookEnvelope);

            if (!ProcessorHookRunner.ShouldDispatch(hooks, hookEnvelope))
            {
                dispatchCompleted = true;
                return envelope.AsPublished(clock.GetUtcNow());
            }

            var stopwatch = Stopwatch.StartNew();
            await dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            OutboxProcessorTelemetry.RecordDispatchDuration(stopwatch.Elapsed);
            var published = envelope.AsPublished(clock.GetUtcNow());
            dispatchCompleted = true;
            return published;
        }
        // A cancelled dispatch leaves the row untouched: no attempt consumed, no error recorded, no visibility moved.
        // The filter also sees a cancellation a dispatcher wrapped in its own exception type.
        catch (Exception exception) when (MediationExceptionFilters.IsCallerCancellation(exception, cancellationToken))
        {
            return null;
        }
        // A handler-internal cancellation (for example a dependency timeout) arrives as OperationCanceledException
        // while the processor token is still live; it is a dispatch failure like any other, not a lease loss.
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var failure = MessageFailure.From(exception, clock.GetUtcNow());

            OutboxProcessorLogMessages.DispatchFailed(
                logger,
                envelope.Id,
                envelope.AttemptCount,
                exception);

            // A refusal or a missing handler produces the same outcome on every attempt, so spending the retry
            // schedule on it only delays the dead-letter entry an operator is waiting to see.
            if (envelope.AttemptCount >= options.Retry.MaxAttempts
                || !MediationExceptionFilters.IsRetryableDispatchException(exception)
                || IsPermanent(options.Retry, exception, envelope.Id, logger))
            {
                return envelope.AsDeadLettered(failure);
            }

            var visibleAfter = clock.GetUtcNow().Add(options.Retry.CalculateDelay(envelope.AttemptCount));
            return envelope.AsFailed(failure, visibleAfter);
        }
        finally
        {
            if (!dispatchCompleted)
            {
                ProcessorHookRunner.RunAbandonDispatchScopes(hooks, hookEnvelope);
            }
        }
    }

    /// <summary>
    ///     Determines whether an application-supplied classifier marks a dispatch failure as permanent.
    /// </summary>
    /// <param name="retry">The retry settings carrying the application classifier.</param>
    /// <param name="exception">The exception that ended the dispatch.</param>
    /// <param name="messageId">The identifier of the message whose failure is classified.</param>
    /// <param name="logger">Records a classifier that throws.</param>
    /// <returns>
    ///     <see langword="true" /> when the classifier recognizes the failure as permanent; otherwise
    ///     <see langword="false" />.
    /// </returns>
    private static bool IsPermanent(RetryOptions retry, Exception exception, Guid messageId, ILogger logger)
    {
        var classifier = retry.IsPermanentFailure;

        if (classifier is null)
        {
            return false;
        }

#pragma warning disable CA1031 // A throwing classifier is treated as non-permanent so a faulty predicate cannot lose a message.
        try
        {
            return classifier(exception);
        }
        catch (Exception classifierException)
        {
            OutboxProcessorLogMessages.PermanentFailureClassifierFailed(logger, messageId, classifierException);
            return false;
        }
#pragma warning restore CA1031
    }

    /// <summary>
    ///     Records a terminal envelope in the pass accumulator.
    /// </summary>
    /// <param name="updated">The post-transition envelope.</param>
    /// <param name="accumulator">The pass accumulator that collects post-transition envelopes.</param>
    private static void RecordTerminalOutcome(
        OutboxEnvelope updated,
        IProcessorPassRecorder<OutboxEnvelope> accumulator)
    {
        switch (updated.Status)
        {
            case OutboxStatus.Published:
                accumulator.RecordSucceeded(updated);
                break;

            case OutboxStatus.Failed:
                accumulator.RecordFailed(updated);
                break;

            case OutboxStatus.DeadLettered:
                accumulator.RecordDeadLettered(updated);
                break;
        }
    }

    /// <summary>
    ///     Records a terminal envelope in the concurrent pass accumulator after persist succeeds.
    /// </summary>
    /// <param name="updated">The post-transition envelope.</param>
    /// <param name="accumulator">The concurrent pass accumulator that collects post-transition envelopes.</param>
    internal static void RecordTerminalOutcome(
        OutboxEnvelope updated,
        ConcurrentProcessorPassAccumulator<OutboxEnvelope> accumulator)
    {
        switch (updated.Status)
        {
            case OutboxStatus.Published:
                accumulator.RecordSucceeded(updated);
                break;

            case OutboxStatus.Failed:
                accumulator.RecordFailed(updated);
                break;

            case OutboxStatus.DeadLettered:
                accumulator.RecordDeadLettered(updated);
                break;
        }
    }
}
