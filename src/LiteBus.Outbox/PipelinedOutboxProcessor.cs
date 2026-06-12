using System;
using System.Collections.Generic;
using System.Threading;
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
///         Each pass writes leased messages to a bounded channel consumed by
///         <see cref="ProcessorOptions.DispatcherConcurrency" /> workers. Lease heartbeat renewal runs while
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
    ///     Gets the shared pipelined processor engine configured for outbox messages.
    /// </summary>
    private readonly PipelinedMessageProcessor<OutboxEnvelope, OutboxProcessorOptions> _processor;

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
        ArgumentNullException.ThrowIfNull(leaseStore);
        ArgumentNullException.ThrowIfNull(stateWriter);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(hooks);

        OutboxProcessorFactory.ValidateOptions(options);

        var resolvedLogger = logger ?? NullLogger<PipelinedOutboxProcessor>.Instance;

        var operations = new OutboxPipelinedMessageProcessorOperations(
            leaseStore,
            stateWriter,
            dispatcher,
            clock,
            hooks,
            resolvedLogger,
            dispatchScopeFactory);

        _processor = new PipelinedMessageProcessor<OutboxEnvelope, OutboxProcessorOptions>(
            leaseStore,
            options,
            clock,
            operations,
            resolvedLogger);
    }

    /// <inheritdoc />
    public Task<ProcessorPassResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        return _processor.ProcessPendingAsync(cancellationToken);
    }
}