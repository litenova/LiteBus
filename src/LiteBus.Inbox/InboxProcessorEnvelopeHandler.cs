using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using Microsoft.Extensions.Logging;

namespace LiteBus.Inbox;

/// <summary>
///     Shared dispatch and terminal transition logic for inbox processor implementations.
/// </summary>
internal static class InboxProcessorEnvelopeHandler
{
    /// <summary>
    ///     Dispatches one leased envelope and records its terminal state for this attempt.
    /// </summary>
    /// <param name="envelope">The leased envelope returned by the store.</param>
    /// <param name="dispatcher">The dispatcher used to execute the envelope.</param>
    /// <param name="options">The retry settings applied after dispatch failures.</param>
    /// <param name="clock">The time provider used for retry visibility timestamps.</param>
    /// <param name="accumulator">The pass accumulator that collects post-transition envelopes.</param>
    /// <param name="logger">The logger used for dispatch failure diagnostics.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch.</param>
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
        IReadOnlyList<IInboxProcessorEnvelopeHook> hooks,
        CancellationToken cancellationToken)
    {
        MessageProcessorDiagnostics.TryGetParentActivityContext(envelope.TraceContext, out var parentContext);
        using var messageActivity = InboxProcessorTelemetry.ActivitySource.StartActivity(
            "inbox.processor.message",
            System.Diagnostics.ActivityKind.Internal,
            parentContext);
        messageActivity?.SetTag("litebus.message_id", envelope.Id);

        try
        {
            await InboxProcessorHookRunner.RunBeforeDispatchAsync(hooks, envelope, cancellationToken).ConfigureAwait(false);
            await dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            await InboxProcessorHookRunner.RunAfterDispatchAsync(hooks, envelope, cancellationToken).ConfigureAwait(false);
            var updated = envelope.AsCompleted();
            accumulator.RecordSucceeded(updated);
            return updated;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = MessageProcessorDiagnostics.FormatError(exception);

            logger.LogWarning(
                exception,
                "Inbox dispatch failed for message {MessageId} on attempt {AttemptCount}.",
                envelope.Id,
                envelope.AttemptCount);

            InboxEnvelope updated;
            if (envelope.AttemptCount >= options.Retry.MaxAttempts)
            {
                updated = envelope.AsDeadLettered(error);
                accumulator.RecordDeadLettered(updated);
            }
            else
            {
                var visibleAfter = clock.GetUtcNow().Add(options.Retry.CalculateDelay(envelope.AttemptCount));
                updated = envelope.AsFailed(error, visibleAfter);
                accumulator.RecordFailed(updated);
            }

            return updated;
        }
    }

    /// <summary>
    ///     Dispatches one leased envelope and records its terminal state for a pipelined pass.
    /// </summary>
    /// <param name="envelope">The leased envelope returned by the store.</param>
    /// <param name="dispatcher">The dispatcher used to execute the envelope.</param>
    /// <param name="options">The retry settings applied after dispatch failures.</param>
    /// <param name="clock">The time provider used for retry visibility timestamps.</param>
    /// <param name="accumulator">The concurrent pass accumulator that collects post-transition envelopes.</param>
    /// <param name="logger">The logger used for dispatch failure diagnostics.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch.</param>
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
        ConcurrentProcessorPassAccumulator<InboxEnvelope> accumulator,
        ILogger logger,
        IReadOnlyList<IInboxProcessorEnvelopeHook> hooks,
        CancellationToken cancellationToken)
    {
        MessageProcessorDiagnostics.TryGetParentActivityContext(envelope.TraceContext, out var parentContext);
        using var messageActivity = InboxProcessorTelemetry.ActivitySource.StartActivity(
            "inbox.processor.message",
            System.Diagnostics.ActivityKind.Internal,
            parentContext);
        messageActivity?.SetTag("litebus.message_id", envelope.Id);

        try
        {
            await InboxProcessorHookRunner.RunBeforeDispatchAsync(hooks, envelope, cancellationToken).ConfigureAwait(false);
            await dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            await InboxProcessorHookRunner.RunAfterDispatchAsync(hooks, envelope, cancellationToken).ConfigureAwait(false);
            var updated = envelope.AsCompleted();
            accumulator.RecordSucceeded(updated);
            return updated;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = MessageProcessorDiagnostics.FormatError(exception);

            logger.LogWarning(
                exception,
                "Inbox dispatch failed for message {MessageId} on attempt {AttemptCount}.",
                envelope.Id,
                envelope.AttemptCount);

            InboxEnvelope updated;
            if (envelope.AttemptCount >= options.Retry.MaxAttempts)
            {
                updated = envelope.AsDeadLettered(error);
                accumulator.RecordDeadLettered(updated);
            }
            else
            {
                var visibleAfter = clock.GetUtcNow().Add(options.Retry.CalculateDelay(envelope.AttemptCount));
                updated = envelope.AsFailed(error, visibleAfter);
                accumulator.RecordFailed(updated);
            }

            return updated;
        }
    }
}
