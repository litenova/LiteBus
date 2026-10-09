using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Messaging.Processing;
using LiteBus.Runtime.Abstractions;
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
    public string ProcessorName { get; } = "inbox";

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
    public void RecordPersistFailed()
    {
        InboxProcessorTelemetry.RecordPersistFailed();
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

        var scope = _dispatchScopeFactory.CreateScope();
        await using var configuredScope = scope.ConfigureAwait(false);
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
    /// <remarks>
    ///     Lease-loss persistence uses a broad <see cref="Exception" /> handler because store faults cannot be enumerated
    ///     safely at the processor boundary. A failure is logged and recorded, and the pass continues with the remaining
    ///     messages instead of aborting the whole batch.
    /// </remarks>
    public async Task PersistLeaseLossOutcomeAsync(
        InboxEnvelope sourceEnvelope,
        ConcurrentProcessorPassAccumulator<InboxEnvelope> accumulator,
        InboxProcessorOptions options,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var visibleAfter = _clock.GetUtcNow().Add(options.Retry.CalculateDelay(sourceEnvelope.AttemptCount));
        var updated = sourceEnvelope.AsFailed(
            MessageFailure.FromError(MessageProcessorDiagnostics.LeaseLostDuringProcessingError, _clock.GetUtcNow()),
            visibleAfter);
        var persistToken = options.HonorShutdownTokenOnPersist ? cancellationToken : CancellationToken.None;

#pragma warning disable CA1031 // One envelope lease-loss persist failure must not abort the entire processor pass.
        PersistResult persistResult;
        try
        {
            persistResult = await _stateWriter.PersistAsync([updated], persistToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            InboxProcessorLogMessages.LeaseLossPersistFailed(logger, sourceEnvelope.Id, exception);
            RecordPersistFailed();
            return;
        }
#pragma warning restore CA1031

        if (persistResult.SkippedCount > 0)
        {
            InboxProcessorTelemetry.RecordPersistSkipped();

            InboxProcessorLogMessages.LeaseLossPersistSkipped(logger, sourceEnvelope.Id);

            return;
        }

        InboxProcessorEnvelopeHandler.RecordTerminalOutcome(updated, accumulator);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     <para>
    ///         AfterDispatch hook failures surface as unrelated exception types, so the handler stays broad and maps the
    ///         failure through the configured hook failure policy instead of enumerating hook exception types.
    ///     </para>
    ///     <para>
    ///         AfterDispatch runs on the persist token rather than the shutdown token. With the default
    ///         <c>HonorShutdownTokenOnPersist = false</c>, a hook that never returns delays shutdown instead of risking a
    ///         duplicate execution after the row was already completed.
    ///     </para>
    /// </remarks>
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
            var afterDispatchHooksCompleted = false;

            // One adapter serves both hook phases below, rather than each building its own.
            var hookEnvelope = new InboxProcessorEnvelopeAdapter(sourceEnvelope);

            try
            {
                await ProcessorHookRunner.RunAfterDispatchAsync(_hooks, hookEnvelope, persistToken)
                    .ConfigureAwait(false);

                afterDispatchHooksCompleted = true;
                terminal = updated;
            }

#pragma warning disable CA1031 // AfterDispatch hook failures surface as unrelated exception types; the hook failure policy maps them.
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (options.HookFailurePolicy == ProcessorHookFailurePolicy.CompleteDespiteHookFailure)
                {
                    InboxProcessorLogMessages.AfterDispatchCompleted(logger, updated.Id, exception);

                    terminal = updated;
                }
                else
                {
                    InboxProcessorLogMessages.AfterDispatchDeadLettered(logger, updated.Id, exception);

                    terminal = sourceEnvelope.AsDeadLettered(MessageFailure.From(exception, _clock.GetUtcNow()));
                }
            }
#pragma warning restore CA1031
            finally
            {
                if (!afterDispatchHooksCompleted)
                {
                    ProcessorHookRunner.RunAbandonDispatchScopes(_hooks, hookEnvelope);
                }
            }

            var persistResult = await _stateWriter.PersistAsync([terminal], persistToken).ConfigureAwait(false);

            if (persistResult.SkippedCount > 0)
            {
                InboxProcessorTelemetry.RecordPersistSkipped();

                InboxProcessorLogMessages.TerminalPersistSkipped(logger, updated.Id);

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

        var outcomePersist = await _stateWriter.PersistAsync([updated], persistToken).ConfigureAwait(false);

        if (outcomePersist.SkippedCount > 0)
        {
            InboxProcessorTelemetry.RecordPersistSkipped();

            InboxProcessorLogMessages.TerminalPersistSkipped(logger, updated.Id);

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

    /// <inheritdoc />
    public long GetLeaseGeneration(InboxEnvelope envelope)
    {
        return envelope.LeaseGeneration;
    }
}
