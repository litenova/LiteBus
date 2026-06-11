using System;
using System.Collections.Generic;
using System.Threading;
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
///         Each pass writes leased envelopes to a bounded channel consumed by
///         <see cref="ProcessorOptions.DispatcherConcurrency" /> workers. Lease heartbeat renewal runs while
///         dispatch is in progress so slow handlers retain ownership until they finish.
///     </para>
///     <para>
///         Successful dispatch runs <c>AfterDispatch</c> hooks while the lease is still held, then persists terminal
///         state. Hook failures dead-letter the message without re-running the handler. Shutdown leaves in-flight
///         envelopes in <c>Processing</c> until the lease expires unless the host drains the processor loop first.
///     </para>
/// </remarks>
public sealed class PipelinedInboxProcessor : IInboxProcessor
{
    /// <summary>
    ///     Gets the shared pipelined processor engine configured for inbox envelopes.
    /// </summary>
    private readonly PipelinedMessageProcessor<InboxEnvelope, InboxProcessorOptions> _processor;

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
        ArgumentNullException.ThrowIfNull(leaseStore);
        ArgumentNullException.ThrowIfNull(stateWriter);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);

        InboxProcessorFactory.ValidateOptions(options);

        var resolvedLogger = logger ?? NullLogger<PipelinedInboxProcessor>.Instance;

        var operations = new InboxPipelinedMessageProcessorOperations(
            leaseStore,
            stateWriter,
            dispatcher,
            clock,
            hooks ?? Array.Empty<IProcessorEnvelopeHook>(),
            resolvedLogger,
            dispatchScopeFactory);

        _processor = new PipelinedMessageProcessor<InboxEnvelope, InboxProcessorOptions>(
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