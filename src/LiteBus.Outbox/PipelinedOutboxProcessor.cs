using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Processing;
using LiteBus.Messaging.Processing;
using LiteBus.Orchestration.Abstractions;
using LiteBus.Outbox.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Outbox;

/// <summary>
///     Pipelined processor that leases a batch, publishes across parallel workers, and persists each terminal outcome
///     immediately.
/// </summary>
/// <remarks>
///     <para>
///         Each pass writes leased messages to a bounded <see cref="Channel{T}" /> consumed by
///         <see cref="OutboxProcessorOptions.DispatcherConcurrency" /> workers. Lease heartbeat renewal runs while
///         publication is in progress so slow dispatchers retain ownership until they finish.
///     </para>
///     <para>
///         Successful publication runs <c>AfterDispatch</c> hooks while the lease is still held, then persists terminal
///         state. Hook failures dead-letter the message without re-publishing. Shutdown leaves in-flight messages in
///         <c>Publishing</c> until the lease expires unless the host drains the processor loop first.
///     </para>
/// </remarks>
public sealed class PipelinedOutboxProcessor : IOutboxProcessor
{
    /// <summary>
    ///     Gets the time provider used for leasing and retry timestamps.
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
    ///     Gets the lease owner name assigned to messages claimed by this processor instance.
    /// </summary>
    private readonly string _leaseOwner;

    /// <summary>
    ///     Gets the batch, lease, owner, and retry settings for this processor instance.
    /// </summary>
    private readonly OutboxProcessorOptions _options;

    /// <summary>
    ///     Gets the lease store used to claim and renew message ownership during processing.
    /// </summary>
    private readonly IOutboxLeaseStore _leaseStore;

    /// <summary>
    ///     Gets the state writer used to persist terminal message transitions.
    /// </summary>
    private readonly IOutboxStateWriter _stateWriter;

    /// <summary>
    ///     Gets the logger used for lease, pass, and dispatch diagnostics.
    /// </summary>
    private readonly ILogger<PipelinedOutboxProcessor> _logger;

    /// <summary>
    ///     Gets the processor envelope hooks invoked around dispatch.
    /// </summary>
    private readonly IReadOnlyList<IProcessorEnvelopeHook> _hooks;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PipelinedOutboxProcessor" /> class.
    /// </summary>
    /// <param name="leaseStore">The lease store used to claim and renew message ownership during processing.</param>
    /// <param name="stateWriter">The state writer used to persist terminal message transitions.</param>
    /// <param name="dispatcher">The dispatcher used to publish leased messages.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    /// <param name="dispatchScopeFactory">The optional scope factory used to resolve scoped dependencies per message.</param>
    public PipelinedOutboxProcessor(
        IOutboxLeaseStore leaseStore,
        IOutboxStateWriter stateWriter,
        IOutboxDispatcher dispatcher,
        OutboxProcessorOptions options,
        TimeProvider clock,
        IReadOnlyList<IProcessorEnvelopeHook> hooks,
        ILogger<PipelinedOutboxProcessor>? logger = null,
        IMessageDispatchScopeFactory? dispatchScopeFactory = null)
    {
        _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
        _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? NullLogger<PipelinedOutboxProcessor>.Instance;
        _hooks = hooks ?? Array.Empty<IProcessorEnvelopeHook>();
        _dispatchScopeFactory = dispatchScopeFactory;
        _leaseOwner = string.IsNullOrWhiteSpace(_options.LeaseOwner)
            ? $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}"
            : _options.LeaseOwner;

        OutboxProcessorFactory.ValidateOptions(_options);
    }

    /// <inheritdoc />
    public async Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        using var passActivity = OutboxProcessorTelemetry.ActivitySource.StartActivity("outbox.processor.pass");

        var stopwatch = ProcessorPassStopwatch.StartNew();
        var now = _clock.GetUtcNow();
        var leasedEnvelopes = await _leaseStore.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = _options.BatchSize,
            LeaseOwner = _leaseOwner,
            Now = now,
            LeaseDuration = _options.LeaseDuration,
            TenantId = _options.TenantId
        }, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug(
            "Leased {LeasedCount} outbox message(s) as owner {LeaseOwner}.",
            leasedEnvelopes.Count,
            _leaseOwner);

        OutboxProcessorTelemetry.RecordLeasesAcquired(leasedEnvelopes.Count);

        if (leasedEnvelopes.Count == 0)
        {
            return OutboxProcessorPassRecorder.FinalizePass(
                new ConcurrentProcessorPassAccumulator<OutboxEnvelope>(),
                0,
                stopwatch.GetElapsedTime(),
                passActivity,
                _logger);
        }

        var channel = Channel.CreateBounded<OutboxEnvelope>(new BoundedChannelOptions(_options.BatchSize)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = true,
            SingleReader = _options.DispatcherConcurrency == 1
        });

        var accumulator = new ConcurrentProcessorPassAccumulator<OutboxEnvelope>();
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

        return OutboxProcessorPassRecorder.FinalizePass(
            accumulator,
            leasedEnvelopes.Count,
            stopwatch.GetElapsedTime(),
            passActivity,
            _logger);
    }

    /// <summary>
    ///     Consumes leased messages from the channel, publishes them, and persists terminal outcomes.
    /// </summary>
    /// <param name="reader">The channel reader supplying leased messages.</param>
    /// <param name="accumulator">The pass accumulator that collects outcomes from this worker.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>A task that completes when the reader is completed.</returns>
    private async Task RunWorkerAsync(
        ChannelReader<OutboxEnvelope> reader,
        ConcurrentProcessorPassAccumulator<OutboxEnvelope> accumulator,
        CancellationToken cancellationToken)
    {
        await foreach (var envelope in reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            OutboxEnvelope? updated;

            try
            {
                updated = await ProcessorLeaseHeartbeat.RunWithHeartbeatAsync(
                    envelope.Id,
                    _leaseOwner,
                    _leaseStore,
                    _options.LeaseDuration,
                    _options.LeaseHeartbeatInterval,
                    _clock,
                    token => DispatchEnvelopeAsync(envelope, accumulator, token),
                    cancellationToken,
                    OutboxProcessorTelemetry.RecordLeaseLost,
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
    ///     Publishes one leased message using an optional per-message dependency injection scope.
    /// </summary>
    /// <param name="envelope">The leased message to publish.</param>
    /// <param name="accumulator">The pass accumulator that collects outcomes from this worker.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch.</param>
    /// <returns>The post-transition envelope or <see langword="null" /> when dispatch was canceled.</returns>
    private async Task<OutboxEnvelope?> DispatchEnvelopeAsync(
        OutboxEnvelope envelope,
        ConcurrentProcessorPassAccumulator<OutboxEnvelope> accumulator,
        CancellationToken cancellationToken)
    {
        if (_dispatchScopeFactory is null)
        {
            return await OutboxProcessorEnvelopeHandler.DispatchAsync(
                envelope,
                _dispatcher,
                _options,
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
            _options,
            _clock,
            _logger,
            _hooks,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs post-dispatch hooks for successful outcomes and persists the resulting terminal envelope.
    /// </summary>
    /// <param name="sourceEnvelope">The leased envelope that was published.</param>
    /// <param name="updated">The post-transition envelope produced by publication.</param>
    /// <param name="accumulator">The pass accumulator that collects outcomes from this worker.</param>
    /// <param name="cancellationToken">A token used to cancel dispatch-side work.</param>
    /// <returns>A task that completes when hook processing and persistence finish.</returns>
    private async Task PersistTerminalOutcomeAsync(
        OutboxEnvelope sourceEnvelope,
        OutboxEnvelope updated,
        ConcurrentProcessorPassAccumulator<OutboxEnvelope> accumulator,
        CancellationToken cancellationToken)
    {
        var persistToken = _options.HonorShutdownTokenOnPersist ? cancellationToken : CancellationToken.None;

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
                var error = MessageProcessorDiagnostics.FormatError(exception);
                _logger.LogWarning(
                    exception,
                    "Outbox AfterDispatch hook failed for message {MessageId}; moving to dead letter.",
                    updated.Id);

                terminal = sourceEnvelope.AsDeadLettered(error);
            }

            var persistResult = await _stateWriter.PersistAsync(new[] { terminal }, persistToken).ConfigureAwait(false);

            if (persistResult.SkippedCount > 0)
            {
                OutboxProcessorTelemetry.RecordPersistSkipped();
                _logger.LogWarning(
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

        var outcomePersist = await _stateWriter.PersistAsync(new[] { updated }, persistToken).ConfigureAwait(false);

        if (outcomePersist.SkippedCount > 0)
        {
            OutboxProcessorTelemetry.RecordPersistSkipped();
            _logger.LogWarning(
                "Outbox terminal persist skipped for message {MessageId} because the active lease was lost.",
                updated.Id);
            return;
        }

        OutboxProcessorEnvelopeHandler.RecordTerminalOutcome(updated, accumulator);
    }
}
