using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Messaging.Processing;
using LiteBus.DurableMessaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace LiteBus.Inbox;

/// <summary>
///     Shared dispatch and terminal transition logic for inbox processor implementations.
/// </summary>
internal static class InboxProcessorEnvelopeHandler
{
    /// <summary>
    ///     Dispatches one leased envelope and returns its terminal state for this attempt.
    /// </summary>
    /// <param name="envelope">The leased envelope returned by the store.</param>
    /// <param name="dispatcher">The dispatcher used to execute the envelope.</param>
    /// <param name="options">The retry settings applied after dispatch failures.</param>
    /// <param name="clock">The time provider used for retry visibility timestamps.</param>
    /// <param name="accumulator">The pass accumulator that collects post-transition envelopes.</param>
    /// <param name="logger">The logger used for dispatch failure diagnostics.</param>
    /// <param name="hooks">The processor envelope hooks invoked before dispatch.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>
    ///     The post-transition envelope when dispatch finished with a terminal outcome for this attempt; otherwise
    ///     <see langword="null" /> when dispatch was canceled.
    /// </returns>
    public static async Task<InboxEnvelope?> ProcessAsync(
        InboxEnvelope envelope,
        IInboxDispatcher dispatcher,
        InboxProcessorOptions options,
        TimeProvider clock,
        IProcessorPassRecorder<InboxEnvelope> accumulator,
        ILogger logger,
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        CancellationToken cancellationToken)
    {
        var updated = await DispatchAsync(
            envelope,
            dispatcher,
            options,
            clock,
            logger,
            hooks,
            cancellationToken).ConfigureAwait(false);

        if (updated is null)
        {
            return null;
        }

        RecordTerminalOutcome(updated, accumulator);
        return updated;
    }

    /// <summary>
    ///     Dispatches one leased envelope and returns its terminal state for a pipelined pass.
    /// </summary>
    /// <param name="envelope">The leased envelope returned by the store.</param>
    /// <param name="dispatcher">The dispatcher used to execute the envelope.</param>
    /// <param name="options">The retry settings applied after dispatch failures.</param>
    /// <param name="clock">The time provider used for retry visibility timestamps.</param>
    /// <param name="logger">The logger used for dispatch failure diagnostics.</param>
    /// <param name="hooks">The processor envelope hooks invoked before dispatch.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>
    ///     The post-transition envelope when dispatch finished with a terminal outcome for this attempt; otherwise
    ///     <see langword="null" /> when dispatch was canceled.
    /// </returns>
    public static Task<InboxEnvelope?> ProcessAsync(
        InboxEnvelope envelope,
        IInboxDispatcher dispatcher,
        InboxProcessorOptions options,
        TimeProvider clock,
        ILogger logger,
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        CancellationToken cancellationToken)
    {
        return DispatchAsync(envelope, dispatcher, options, clock, logger, hooks, cancellationToken);
    }

    /// <summary>
    ///     Executes dispatch and maps failures to terminal retry or dead-letter transitions.
    /// </summary>
    /// <param name="envelope">The leased envelope returned by the store.</param>
    /// <param name="dispatcher">The dispatcher used to execute the envelope.</param>
    /// <param name="options">The retry settings applied after dispatch failures.</param>
    /// <param name="clock">The time provider used for retry visibility timestamps.</param>
    /// <param name="logger">The logger used for dispatch failure diagnostics.</param>
    /// <param name="hooks">The processor envelope hooks invoked before dispatch.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>
    ///     The post-transition envelope when dispatch finished with a terminal outcome for this attempt; otherwise
    ///     <see langword="null" /> when dispatch was canceled.
    /// </returns>
    private static async Task<InboxEnvelope?> DispatchAsync(
        InboxEnvelope envelope,
        IInboxDispatcher dispatcher,
        InboxProcessorOptions options,
        TimeProvider clock,
        ILogger logger,
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        CancellationToken cancellationToken)
    {
        MessageProcessorDiagnostics.TryGetParentActivityContext(envelope.TraceContext, out var parentContext);

        using var messageActivity = InboxProcessorTelemetry.ActivitySource.StartActivity(
            "inbox.processor.message",
            ActivityKind.Internal,
            parentContext);

        messageActivity?.SetTag("litebus.message_id", envelope.Id);
        var dispatchCompleted = false;

        try
        {
            await InboxProcessorHookRunner.RunBeforeDispatchAsync(hooks, envelope, cancellationToken).ConfigureAwait(false);
            InboxProcessorHookRunner.RunPrepareDispatchScope(hooks, envelope);

            if (!InboxProcessorHookRunner.ShouldDispatch(hooks, envelope))
            {
                dispatchCompleted = true;
                return envelope.AsCompleted();
            }

            var stopwatch = Stopwatch.StartNew();
            await dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            InboxProcessorTelemetry.RecordDispatchDuration(stopwatch.Elapsed);
            var completed = envelope.AsCompleted();
            dispatchCompleted = true;
            return completed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        // Handler, mediator, and hook failures surface as unrelated exception types; map them to retry or dead-letter transitions.
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = MessageProcessorDiagnostics.FormatError(exception);

            InboxProcessorLogMessages.DispatchFailed(
                logger,
                envelope.Id,
                envelope.AttemptCount,
                exception);

            // A refusal or a missing handler produces the same outcome on every attempt, so spending the retry
            // schedule on it only delays the dead-letter entry an operator is waiting to see.
            if (envelope.AttemptCount >= options.Retry.MaxAttempts
                || !MediationExceptionFilters.IsRetryableDispatchException(exception))
            {
                return envelope.AsDeadLettered(error);
            }

            var visibleAfter = clock.GetUtcNow().Add(options.Retry.CalculateDelay(envelope.AttemptCount));
            return envelope.AsFailed(error, visibleAfter);
        }
        finally
        {
            if (!dispatchCompleted)
            {
                InboxProcessorHookRunner.RunAbandonDispatchScopes(hooks, envelope);
            }
        }
    }

    /// <summary>
    ///     Records a terminal envelope in the pass accumulator when dispatch failed.
    /// </summary>
    /// <param name="updated">The post-transition envelope.</param>
    /// <param name="accumulator">The pass accumulator that collects post-transition envelopes.</param>
    private static void RecordTerminalOutcome(
        InboxEnvelope updated,
        IProcessorPassRecorder<InboxEnvelope> accumulator)
    {
        switch (updated.Status)
        {
            case InboxStatus.Completed:
                accumulator.RecordSucceeded(updated);
                break;

            case InboxStatus.Failed:
                accumulator.RecordFailed(updated);
                break;

            case InboxStatus.DeadLettered:
                accumulator.RecordDeadLettered(updated);
                break;
        }
    }

    /// <summary>
    ///     Records a terminal envelope in the concurrent pass accumulator.
    /// </summary>
    /// <param name="updated">The post-transition envelope.</param>
    /// <param name="accumulator">The concurrent pass accumulator that collects post-transition envelopes.</param>
    internal static void RecordTerminalOutcome(
        InboxEnvelope updated,
        ConcurrentProcessorPassAccumulator<InboxEnvelope> accumulator)
    {
        switch (updated.Status)
        {
            case InboxStatus.Completed:
                accumulator.RecordSucceeded(updated);
                break;

            case InboxStatus.Failed:
                accumulator.RecordFailed(updated);
                break;

            case InboxStatus.DeadLettered:
                accumulator.RecordDeadLettered(updated);
                break;
        }
    }
}
