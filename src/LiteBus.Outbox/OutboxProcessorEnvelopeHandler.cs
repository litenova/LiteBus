using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
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
        MessageProcessorDiagnostics.TryGetParentActivityContext(envelope.TraceContext, out var parentContext);
        using var messageActivity = OutboxProcessorTelemetry.ActivitySource.StartActivity(
            "outbox.processor.message",
            System.Diagnostics.ActivityKind.Internal,
            parentContext);
        messageActivity?.SetTag("litebus.message_id", envelope.Id);

        try
        {
            await dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            var updated = envelope.AsPublished();
            accumulator.RecordSucceeded(updated);
            return updated;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = MessageProcessorDiagnostics.FormatError(exception);

            logger.LogWarning(
                exception,
                "Outbox dispatch failed for message {MessageId} on attempt {AttemptCount}.",
                envelope.Id,
                envelope.AttemptCount);

            OutboxEnvelope updated;
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
    ///     Publishes one leased message envelope and records its terminal state for a pipelined pass.
    /// </summary>
    /// <param name="envelope">The leased outbox message returned by the store.</param>
    /// <param name="dispatcher">The dispatcher used to publish the message.</param>
    /// <param name="options">The retry settings applied after publication failures.</param>
    /// <param name="clock">The time provider used for retry visibility timestamps.</param>
    /// <param name="accumulator">The concurrent pass accumulator that collects post-transition envelopes.</param>
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
        ConcurrentProcessorPassAccumulator<OutboxEnvelope> accumulator,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        MessageProcessorDiagnostics.TryGetParentActivityContext(envelope.TraceContext, out var parentContext);
        using var messageActivity = OutboxProcessorTelemetry.ActivitySource.StartActivity(
            "outbox.processor.message",
            System.Diagnostics.ActivityKind.Internal,
            parentContext);
        messageActivity?.SetTag("litebus.message_id", envelope.Id);

        try
        {
            await dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            var updated = envelope.AsPublished();
            accumulator.RecordSucceeded(updated);
            return updated;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = MessageProcessorDiagnostics.FormatError(exception);

            logger.LogWarning(
                exception,
                "Outbox dispatch failed for message {MessageId} on attempt {AttemptCount}.",
                envelope.Id,
                envelope.AttemptCount);

            OutboxEnvelope updated;
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
