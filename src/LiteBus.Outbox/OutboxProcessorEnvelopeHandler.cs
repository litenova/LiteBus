using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Messaging.Processing;
using LiteBus.Orchestration.Abstractions;
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

        try
        {
            await OutboxProcessorHookRunner.RunBeforeDispatchAsync(hooks, envelope, cancellationToken)
                .ConfigureAwait(false);

            OutboxProcessorHookRunner.RunPrepareDispatchScope(hooks, envelope);

            var stopwatch = Stopwatch.StartNew();
            await dispatcher.DispatchAsync(envelope, cancellationToken).ConfigureAwait(false);
            stopwatch.Stop();
            OutboxProcessorTelemetry.RecordDispatchDuration(stopwatch.Elapsed);
            var published = envelope.AsPublished() with { PublishedAt = clock.GetUtcNow() };
            dispatchCompleted = true;
            return published;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            var error = MessageProcessorDiagnostics.FormatError(exception);

            OutboxProcessorLogMessages.DispatchFailed(
                logger,
                envelope.Id,
                envelope.AttemptCount,
                exception);

            if (envelope.AttemptCount >= options.Retry.MaxAttempts)
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
                OutboxProcessorHookRunner.RunAbandonDispatchScopes(hooks, envelope);
            }
        }
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
