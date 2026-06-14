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
///     Supplies outbox-specific leasing, dispatch, persistence, and telemetry for the shared pipelined processor.
/// </summary>
internal sealed class OutboxPipelinedMessageProcessorOperations : IPipelinedMessageProcessorOperations<OutboxEnvelope, OutboxProcessorOptions>
{
    /// <summary>
    ///     Gets the time provider used for retry timestamps during dispatch.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the dispatcher used to publish each leased message.
    /// </summary>
    private readonly IOutboxDispatcher _dispatcher;

    /// <summary>
    ///     Gets the optional scope factory used to resolve scoped dependencies per message.
    /// </summary>
    private readonly IMessageDispatchScopeFactory? _dispatchScopeFactory;

    /// <summary>
    ///     Gets the processor envelope hooks invoked around dispatch.
    /// </summary>
    private readonly IReadOnlyList<IProcessorEnvelopeHook> _hooks;

    /// <summary>
    ///     Gets the lease store used to claim messages during processing.
    /// </summary>
    private readonly IOutboxLeaseStore _leaseStore;

    /// <summary>
    ///     Gets the logger used for publication failure diagnostics.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    ///     Gets the state writer used to persist terminal message transitions.
    /// </summary>
    private readonly IOutboxStateWriter _stateWriter;

    /// <summary>
    ///     Initializes a new instance of the <see cref="OutboxPipelinedMessageProcessorOperations" /> class.
    /// </summary>
    /// <param name="leaseStore">The lease store used to claim messages during processing.</param>
    /// <param name="stateWriter">The state writer used to persist terminal message transitions.</param>
    /// <param name="dispatcher">The dispatcher used to publish each leased message.</param>
    /// <param name="clock">The time provider used for retry timestamps during dispatch.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch.</param>
    /// <param name="logger">The logger used for publication failure diagnostics.</param>
    /// <param name="dispatchScopeFactory">The optional scope factory used to resolve scoped dependencies per message.</param>
    public OutboxPipelinedMessageProcessorOperations(
        IOutboxLeaseStore leaseStore,
        IOutboxStateWriter stateWriter,
        IOutboxDispatcher dispatcher,
        TimeProvider clock,
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        ILogger logger,
        IMessageDispatchScopeFactory? dispatchScopeFactory = null)
    {
        ArgumentNullException.ThrowIfNull(leaseStore);

        _leaseStore = leaseStore;
        ArgumentNullException.ThrowIfNull(stateWriter);

        _stateWriter = stateWriter;
        ArgumentNullException.ThrowIfNull(dispatcher);

        _dispatcher = dispatcher;
        ArgumentNullException.ThrowIfNull(clock);

        _clock = clock;
        ArgumentNullException.ThrowIfNull(hooks);
        _hooks = hooks;
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _dispatchScopeFactory = dispatchScopeFactory;
    }

    /// <inheritdoc />
    public string LeaseRenewalFailedMessage { get; } =
        "Outbox lease renewal failed for message {MessageId} owned by {LeaseOwner}; canceling dispatch.";

    /// <inheritdoc />
    public string LeasedBatchDebugMessage { get; } =
        "Leased {LeasedCount} outbox message(s) as owner {LeaseOwner}.";

    /// <inheritdoc />
    public Activity? StartPassActivity()
    {
        return OutboxProcessorTelemetry.ActivitySource.StartActivity(
            "outbox.processor.pass",
            ActivityKind.Internal,
            default(ActivityContext));
    }

    /// <inheritdoc />
    public void RecordLeasesAcquired(int count)
    {
        OutboxProcessorTelemetry.RecordLeasesAcquired(count);
    }

    /// <inheritdoc />
    public void RecordLeaseLost()
    {
        OutboxProcessorTelemetry.RecordLeaseLost();
    }

    /// <inheritdoc />
    public void RecordPersistFailed()
    {
        OutboxProcessorTelemetry.RecordPersistFailed();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<OutboxEnvelope>> LeasePendingAsync(
        string leaseOwner,
        OutboxProcessorOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return _leaseStore.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = options.BatchSize,
            LeaseOwner = leaseOwner,
            Now = now,
            LeaseDuration = options.LeaseDuration,
            TenantId = options.TenantId
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<OutboxEnvelope?> DispatchEnvelopeAsync(
        OutboxEnvelope envelope,
        OutboxProcessorOptions options,
        CancellationToken cancellationToken)
    {
        if (_dispatchScopeFactory is null)
        {
            return await OutboxProcessorEnvelopeHandler.DispatchAsync(
                envelope,
                _dispatcher,
                options,
                _clock,
                _logger,
                _hooks,
                cancellationToken).ConfigureAwait(false);
        }

        using var scope = _dispatchScopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetService(typeof(IOutboxDispatcher)) as IOutboxDispatcher ?? _dispatcher;

        return await OutboxProcessorEnvelopeHandler.DispatchAsync(
            envelope,
            dispatcher,
            options,
            _clock,
            _logger,
            _hooks,
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task PersistLeaseLossOutcomeAsync(
        OutboxEnvelope sourceEnvelope,
        ConcurrentProcessorPassAccumulator<OutboxEnvelope> accumulator,
        OutboxProcessorOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var visibleAfter = _clock.GetUtcNow().Add(options.Retry.CalculateDelay(sourceEnvelope.AttemptCount));
        var updated = sourceEnvelope.AsFailed(MessageProcessorDiagnostics.LeaseLostDuringProcessingError, visibleAfter);
        var persistToken = options.HonorShutdownTokenOnPersist ? cancellationToken : CancellationToken.None;

        var persistResult = await _stateWriter.PersistAsync([updated], persistToken).ConfigureAwait(false);

        if (persistResult.SkippedCount > 0)
        {
            OutboxProcessorTelemetry.RecordPersistSkipped();

            logger.LogWarning(
                "Outbox lease-loss persist skipped for message {MessageId} because the active lease was lost.",
                sourceEnvelope.Id);

            return;
        }

        OutboxProcessorEnvelopeHandler.RecordTerminalOutcome(updated, accumulator);
    }

    /// <inheritdoc />
    public async Task PersistTerminalOutcomeAsync(
        OutboxEnvelope sourceEnvelope,
        OutboxEnvelope updated,
        ConcurrentProcessorPassAccumulator<OutboxEnvelope> accumulator,
        OutboxProcessorOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var persistToken = options.HonorShutdownTokenOnPersist ? cancellationToken : CancellationToken.None;

        if (updated.Status == OutboxStatus.Published)
        {
            OutboxEnvelope terminal;

            try
            {
                await OutboxProcessorHookRunner.RunAfterDispatchAsync(_hooks, sourceEnvelope, cancellationToken)
                    .ConfigureAwait(false);

                terminal = updated;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (options.HookFailurePolicy == ProcessorHookFailurePolicy.CompleteDespiteHookFailure)
                {
                    logger.LogWarning(
                        exception,
                        "Outbox AfterDispatch hook failed for message {MessageId}; completing dispatch despite hook failure.",
                        updated.Id);

                    terminal = updated;
                }
                else
                {
                    var error = MessageProcessorDiagnostics.FormatError(exception);

                    logger.LogWarning(
                        exception,
                        "Outbox AfterDispatch hook failed for message {MessageId}; moving to dead letter.",
                        updated.Id);

                    terminal = sourceEnvelope.AsDeadLettered(error);
                }
            }

            var persistResult = await _stateWriter.PersistAsync([terminal], persistToken).ConfigureAwait(false);

            if (persistResult.SkippedCount > 0)
            {
                OutboxProcessorTelemetry.RecordPersistSkipped();

                logger.LogWarning(
                    "Outbox terminal persist skipped for message {MessageId} because the active lease was lost.",
                    updated.Id);

                return;
            }

            if (terminal.Status == OutboxStatus.Published)
            {
                accumulator.RecordSucceeded(terminal);
            }
            else
            {
                accumulator.RecordDeadLettered(terminal);
            }

            return;
        }

        var outcomePersist = await _stateWriter.PersistAsync([updated], persistToken).ConfigureAwait(false);

        if (outcomePersist.SkippedCount > 0)
        {
            OutboxProcessorTelemetry.RecordPersistSkipped();

            logger.LogWarning(
                "Outbox terminal persist skipped for message {MessageId} because the active lease was lost.",
                updated.Id);

            return;
        }

        OutboxProcessorEnvelopeHandler.RecordTerminalOutcome(updated, accumulator);
    }

    /// <inheritdoc />
    public ProcessorPassResult FinalizePass(
        ConcurrentProcessorPassAccumulator<OutboxEnvelope> accumulator,
        int leasedCount,
        TimeSpan elapsed,
        Activity? passActivity,
        ILogger logger)
    {
        return OutboxProcessorPassRecorder.FinalizePass(accumulator, leasedCount, elapsed, passActivity, logger);
    }

    /// <inheritdoc />
    public Guid GetMessageId(OutboxEnvelope envelope)
    {
        return envelope.Id;
    }
}