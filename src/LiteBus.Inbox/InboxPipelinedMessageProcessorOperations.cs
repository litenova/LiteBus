using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Messaging.Processing;
using LiteBus.Orchestration.Abstractions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Inbox;

/// <summary>
///     Supplies inbox-specific leasing, dispatch, persistence, and telemetry for the shared pipelined processor.
/// </summary>
internal sealed class InboxPipelinedMessageProcessorOperations : IPipelinedMessageProcessorOperations<InboxEnvelope, InboxProcessorOptions>
{
    /// <summary>
    ///     Gets the time provider used for retry timestamps during dispatch.
    /// </summary>
    private readonly TimeProvider _clock;

    /// <summary>
    ///     Gets the dispatcher used to execute each leased envelope.
    /// </summary>
    private readonly IInboxDispatcher _dispatcher;

    /// <summary>
    ///     Gets the optional scope factory used to resolve scoped handlers per message.
    /// </summary>
    private readonly IMessageDispatchScopeFactory? _dispatchScopeFactory;

    /// <summary>
    ///     Gets the processor envelope hooks invoked around dispatch.
    /// </summary>
    private readonly IReadOnlyList<IProcessorEnvelopeHook> _hooks;

    /// <summary>
    ///     Gets the lease store used to claim envelopes during processing.
    /// </summary>
    private readonly IInboxLeaseStore _leaseStore;

    /// <summary>
    ///     Gets the logger used for dispatch failure diagnostics.
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    ///     Gets the state writer used to persist terminal envelope transitions.
    /// </summary>
    private readonly IInboxStateWriter _stateWriter;

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxPipelinedMessageProcessorOperations" /> class.
    /// </summary>
    /// <param name="leaseStore">The lease store used to claim envelopes during processing.</param>
    /// <param name="stateWriter">The state writer used to persist terminal envelope transitions.</param>
    /// <param name="dispatcher">The dispatcher used to execute each leased envelope.</param>
    /// <param name="clock">The time provider used for retry timestamps during dispatch.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch.</param>
    /// <param name="logger">The logger used for dispatch failure diagnostics.</param>
    /// <param name="dispatchScopeFactory">The optional scope factory used to resolve scoped handlers per message.</param>
    public InboxPipelinedMessageProcessorOperations(
        IInboxLeaseStore leaseStore,
        IInboxStateWriter stateWriter,
        IInboxDispatcher dispatcher,
        TimeProvider clock,
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        ILogger logger,
        IMessageDispatchScopeFactory? dispatchScopeFactory = null)
    {
        _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        ArgumentNullException.ThrowIfNull(hooks);
        _hooks = hooks;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _dispatchScopeFactory = dispatchScopeFactory;
    }

    /// <inheritdoc />
    public string LeaseRenewalFailedMessage { get; } =
        "Inbox lease renewal failed for message {MessageId} owned by {LeaseOwner}; canceling dispatch.";

    /// <inheritdoc />
    public string LeasedBatchDebugMessage { get; } =
        "Leased {LeasedCount} inbox envelope(s) as owner {LeaseOwner}.";

    /// <inheritdoc />
    public Activity? StartPassActivity()
    {
        return InboxProcessorTelemetry.ActivitySource.StartActivity(
            "inbox.processor.pass",
            ActivityKind.Internal,
            default(ActivityContext));
    }

    /// <inheritdoc />
    public void RecordLeasesAcquired(int count)
    {
        InboxProcessorTelemetry.RecordLeasesAcquired(count);
    }

    /// <inheritdoc />
    public void RecordLeaseLost()
    {
        InboxProcessorTelemetry.RecordLeaseLost();
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
        string leaseOwner,
        InboxProcessorOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return _leaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = options.BatchSize,
            LeaseOwner = leaseOwner,
            Now = now,
            LeaseDuration = options.LeaseDuration,
            TenantId = options.TenantId
        }, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<InboxEnvelope?> DispatchEnvelopeAsync(
        InboxEnvelope envelope,
        InboxProcessorOptions options,
        CancellationToken cancellationToken)
    {
        if (_dispatchScopeFactory is null)
        {
            return await InboxProcessorEnvelopeHandler.ProcessAsync(
                envelope,
                _dispatcher,
                options,
                _clock,
                _logger,
                _hooks,
                cancellationToken).ConfigureAwait(false);
        }

        using var scope = _dispatchScopeFactory.CreateScope();
        var dispatcher = scope.ServiceProvider.GetService(typeof(IInboxDispatcher)) as IInboxDispatcher ?? _dispatcher;

        return await InboxProcessorEnvelopeHandler.ProcessAsync(
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
        InboxEnvelope sourceEnvelope,
        ConcurrentProcessorPassAccumulator<InboxEnvelope> accumulator,
        InboxProcessorOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var visibleAfter = _clock.GetUtcNow().Add(options.Retry.CalculateDelay(sourceEnvelope.AttemptCount));
        var updated = sourceEnvelope.AsFailed(MessageProcessorDiagnostics.LeaseLostDuringProcessingError, visibleAfter);
        var persistToken = options.HonorShutdownTokenOnPersist ? cancellationToken : CancellationToken.None;

        var persistResult = await _stateWriter.PersistAsync(new[] { updated }, persistToken).ConfigureAwait(false);

        if (persistResult.SkippedCount > 0)
        {
            InboxProcessorTelemetry.RecordPersistSkipped();

            logger.LogWarning(
                "Inbox lease-loss persist skipped for message {MessageId} because the active lease was lost.",
                sourceEnvelope.Id);

            return;
        }

        InboxProcessorEnvelopeHandler.RecordTerminalOutcome(updated, accumulator);
    }

    /// <inheritdoc />
    public async Task PersistTerminalOutcomeAsync(
        InboxEnvelope sourceEnvelope,
        InboxEnvelope updated,
        ConcurrentProcessorPassAccumulator<InboxEnvelope> accumulator,
        InboxProcessorOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var persistToken = options.HonorShutdownTokenOnPersist ? cancellationToken : CancellationToken.None;

        if (updated.Status == InboxStatus.Completed)
        {
            InboxEnvelope terminal;

            try
            {
                await InboxProcessorHookRunner.RunAfterDispatchAsync(_hooks, sourceEnvelope, cancellationToken)
                    .ConfigureAwait(false);

                terminal = updated;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (options.HookFailurePolicy == ProcessorHookFailurePolicy.CompleteDespiteHookFailure)
                {
                    logger.LogWarning(
                        exception,
                        "Inbox AfterDispatch hook failed for message {MessageId}; completing dispatch despite hook failure.",
                        updated.Id);

                    terminal = updated;
                }
                else
                {
                    var error = MessageProcessorDiagnostics.FormatError(exception);

                    logger.LogWarning(
                        exception,
                        "Inbox AfterDispatch hook failed for message {MessageId}; moving to dead letter.",
                        updated.Id);

                    terminal = sourceEnvelope.AsDeadLettered(error);
                }
            }

            var persistResult = await _stateWriter.PersistAsync(new[] { terminal }, persistToken).ConfigureAwait(false);

            if (persistResult.SkippedCount > 0)
            {
                InboxProcessorTelemetry.RecordPersistSkipped();

                logger.LogWarning(
                    "Inbox terminal persist skipped for message {MessageId} because the active lease was lost.",
                    updated.Id);

                return;
            }

            if (terminal.Status == InboxStatus.Completed)
            {
                accumulator.RecordSucceeded(terminal);
            }
            else
            {
                accumulator.RecordDeadLettered(terminal);
            }

            return;
        }

        var outcomePersist = await _stateWriter.PersistAsync(new[] { updated }, persistToken).ConfigureAwait(false);

        if (outcomePersist.SkippedCount > 0)
        {
            InboxProcessorTelemetry.RecordPersistSkipped();

            logger.LogWarning(
                "Inbox terminal persist skipped for message {MessageId} because the active lease was lost.",
                updated.Id);

            return;
        }

        InboxProcessorEnvelopeHandler.RecordTerminalOutcome(updated, accumulator);
    }

    /// <inheritdoc />
    public ProcessorPassResult FinalizePass(
        ConcurrentProcessorPassAccumulator<InboxEnvelope> accumulator,
        int leasedCount,
        TimeSpan elapsed,
        Activity? passActivity,
        ILogger logger)
    {
        return InboxProcessorPassRecorder.FinalizePass(accumulator, leasedCount, elapsed, passActivity, logger);
    }

    /// <inheritdoc />
    public Guid GetMessageId(InboxEnvelope envelope)
    {
        return envelope.Id;
    }
}