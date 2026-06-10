using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Messaging.Processing;
using LiteBus.Orchestration.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Inbox;

/// <summary>
///     Pipelined processor that leases a batch, dispatches across parallel workers, and persists each terminal outcome
///     immediately.
/// </summary>
/// <remarks>
///     <para>
///         Each pass writes leased envelopes to a bounded <see cref="Channel{T}" /> consumed by
///         <see cref="InboxProcessorOptions.DispatcherConcurrency" /> workers. Lease heartbeat renewal runs while
///         dispatch is in progress so slow handlers retain ownership until they finish.
///     </para>
///     <para>
///         Successful dispatch runs <c>AfterDispatch</c> hooks while the lease is still held, then persists terminal
///         state. Hook failures dead-letter the message without re-running the handler. Shutdown leaves in-flight
///         envelopes in <c>Processing</c> until the lease expires unless the host drains the processor loop first.
///     </para>
/// </remarks>
public sealed class PipelinedInboxProcessor : Abstractions.IInboxProcessor
{
    /// <summary>
    ///     Gets the time provider used for leasing and retry timestamps.
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
    ///     Gets the lease owner name assigned to envelopes claimed by this processor instance.
    /// </summary>
    private readonly string _leaseOwner;

    /// <summary>
    ///     Gets the batch, lease, owner, and retry settings for this processor instance.
    /// </summary>
    private readonly InboxProcessorOptions _options;

    /// <summary>
    ///     Gets the lease store used to claim and renew envelope ownership during processing.
    /// </summary>
    private readonly IInboxLeaseStore _leaseStore;

    /// <summary>
    ///     Gets the state writer used to persist terminal envelope transitions.
    /// </summary>
    private readonly IInboxStateWriter _stateWriter;

    /// <summary>
    ///     Gets the logger used for lease, pass, and dispatch diagnostics.
    /// </summary>
    private readonly ILogger<PipelinedInboxProcessor> _logger;

    /// <summary>
    ///     Gets the processor envelope hooks invoked around dispatch.
    /// </summary>
    private readonly IReadOnlyList<IProcessorEnvelopeHook> _hooks;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelinedInboxProcessor" /> class.
    /// </summary>
    /// <param name="leaseStore">The lease store used to claim and renew envelope ownership during processing.</param>
    /// <param name="stateWriter">The state writer used to persist terminal envelope transitions.</param>
    /// <param name="dispatcher">The dispatcher used to execute each leased envelope.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    /// <param name="dispatchScopeFactory">The optional scope factory used to resolve scoped handlers per message.</param>
    public PipelinedInboxProcessor(
        IInboxLeaseStore leaseStore,
        IInboxStateWriter stateWriter,
        IInboxDispatcher dispatcher,
        InboxProcessorOptions options,
        TimeProvider clock,
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        ILogger<PipelinedInboxProcessor>? logger = null,
        IMessageDispatchScopeFactory? dispatchScopeFactory = null)
    {
        _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<PipelinedInboxProcessor>.Instance;
        _hooks = hooks ?? Array.Empty<IProcessorEnvelopeHook>();
        _dispatchScopeFactory = dispatchScopeFactory;
        _leaseOwner = string.IsNullOrWhiteSpace(_options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : _options.LeaseOwner;

        InboxProcessorFactory.ValidateOptions(_options);
    }

    /// <inheritdoc />
    public async Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        using var passActivity = InboxProcessorTelemetry.ActivitySource.StartActivity("inbox.processor.pass");

        var stopwatch = ProcessorPassStopwatch.StartNew();
        var now = _clock.GetUtcNow();
        var leasedEnvelopes = await _leaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = _options.BatchSize,
            LeaseOwner = _leaseOwner,
            Now = now,
            LeaseDuration = _options.LeaseDuration,
            TenantId = _options.TenantId
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Leased {LeasedCount} inbox envelope(s) as owner {LeaseOwner}.",
            leasedEnvelopes.Count,
            _leaseOwner);

        InboxProcessorTelemetry.RecordLeasesAcquired(leasedEnvelopes.Count);

        if (leasedEnvelopes.Count == 0)
        {
            return InboxProcessorPassRecorder.FinalizePass(
                new ConcurrentProcessorPassAccumulator<InboxEnvelope>(),
                0,
                stopwatch.GetElapsedTime(),
                passActivity,
                _logger);
        }

        var channel = Channel.CreateBounded<InboxEnvelope>(new BoundedChannelOptions(_options.BatchSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = _options.DispatcherConcurrency == 1
        });

        var accumulator = new ConcurrentProcessorPassAccumulator<InboxEnvelope>();
        var workers = new Task[_options.DispatcherConcurrency];

        for (var index = 0; index < workers.Length; index++)
        {
            workers[index] = RunWorkerAsync(channel.Reader, accumulator, cancellationToken);
        }

        try
        {
            foreach (var envelope in leasedEnvelopes)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await channel.Writer.WriteAsync(envelope, cancellationToken).ConfigureAwait(false);
            }

            channel.Writer.Complete();
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            channel.Writer.TryComplete();

            try
            {
                await Task.WhenAll(workers).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            throw;
        }

        return InboxProcessorPassRecorder.FinalizePass(
            accumulator,
            leasedEnvelopes.Count,
            stopwatch.GetElapsedTime(),
            passActivity,
            _logger);
    }

    /// <summary>
    ///     Consumes leased envelopes from the channel, dispatches them, and persists terminal outcomes.
    /// </summary>
    /// <param name="reader">The channel reader supplying leased envelopes.</param>
    /// <param name="accumulator">The pass accumulator that collects outcomes from this worker.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>A task that completes when the reader is completed.</returns>
    private async Task RunWorkerAsync(
        ChannelReader<InboxEnvelope> reader,
        ConcurrentProcessorPassAccumulator<InboxEnvelope> accumulator,
        CancellationToken cancellationToken)
    {
        await foreach (var envelope in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            InboxEnvelope? updated;

            try
            {
                updated = await ProcessorLeaseHeartbeat.RunWithHeartbeatAsync(
                    envelope.Id,
                    _leaseOwner,
                    _leaseStore,
                    _options.LeaseDuration,
                    _options.LeaseHeartbeatInterval,
                    _clock,
                    token => DispatchEnvelopeAsync(envelope, token),
                    cancellationToken,
                    InboxProcessorTelemetry.RecordLeaseLost,
                    _logger).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OperationCanceledException)
            {
                continue;
            }

            if (updated is null)
            {
                continue;
            }

            await PersistTerminalOutcomeAsync(envelope, updated, accumulator, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Dispatches one leased envelope using an optional per-message dependency injection scope.
    /// </summary>
    /// <param name="envelope">The leased envelope to dispatch.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>The post-transition envelope or <see langword="null" /> when dispatch was canceled.</returns>
    private async Task<InboxEnvelope?> DispatchEnvelopeAsync(InboxEnvelope envelope, CancellationToken cancellationToken)
    {
        if (_dispatchScopeFactory is null)
        {
            return await InboxProcessorEnvelopeHandler.ProcessAsync(
                envelope,
                _dispatcher,
                _options,
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
            _options,
            _clock,
            _logger,
            _hooks,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs post-dispatch hooks for successful outcomes and persists the resulting terminal envelope.
    /// </summary>
    /// <param name="sourceEnvelope">The leased envelope that was dispatched.</param>
    /// <param name="updated">The post-transition envelope produced by dispatch.</param>
    /// <param name="accumulator">The pass accumulator that collects outcomes from this worker.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch-side work.</param>
    /// <returns>A task that completes when hook processing and persistence finish.</returns>
    private async Task PersistTerminalOutcomeAsync(
        InboxEnvelope sourceEnvelope,
        InboxEnvelope updated,
        ConcurrentProcessorPassAccumulator<InboxEnvelope> accumulator,
        CancellationToken cancellationToken)
    {
        var persistToken = _options.HonorShutdownTokenOnPersist ? cancellationToken : CancellationToken.None;

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
                var error = MessageProcessorDiagnostics.FormatError(exception);
                _logger.LogWarning(
                    exception,
                    "Inbox AfterDispatch hook failed for message {MessageId}; moving to dead letter.",
                    updated.Id);

                terminal = sourceEnvelope.AsDeadLettered(error);
            }

            var persistResult = await _stateWriter.PersistAsync(new[] { terminal }, persistToken).ConfigureAwait(false);

            if (persistResult.SkippedCount > 0)
            {
                InboxProcessorTelemetry.RecordPersistSkipped();
                _logger.LogWarning(
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
            _logger.LogWarning(
                "Inbox terminal persist skipped for message {MessageId} because the active lease was lost.",
                updated.Id);
            return;
        }

        InboxProcessorEnvelopeHandler.RecordTerminalOutcome(updated, accumulator);
    }

}
