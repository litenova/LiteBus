using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using Microsoft.Extensions.Logging;

namespace LiteBus.Inbox;

/// <summary>
///     Obsolete alias for <see cref="LegacySequentialInboxProcessor" /> retained for backward compatibility.
/// </summary>
[Obsolete("Use LegacySequentialInboxProcessor or configure InboxProcessorOptions.Architecture instead.")]
public sealed class InboxProcessor : LegacySequentialInboxProcessor
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxProcessor" /> class.
    /// </summary>
    /// <param name="processingStore">The processing store used to lease and persist envelope state transitions.</param>
    /// <param name="dispatcher">The dispatcher used to execute each leased envelope.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="hooks">The processor envelope hooks invoked around dispatch.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    public InboxProcessor(
        IInboxProcessingStore processingStore,
        IInboxDispatcher dispatcher,
        InboxProcessorOptions options,
        TimeProvider clock,
        IReadOnlyList<IInboxProcessorEnvelopeHook>? hooks = null,
        ILogger<InboxProcessor>? logger = null)
        : base(processingStore, dispatcher, options, clock, hooks ?? Array.Empty<IInboxProcessorEnvelopeHook>(), logger)
    {
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="InboxProcessor" /> class using separate lease and state writer roles.
    /// </summary>
    /// <param name="leaseStore">The store role used to lease due envelopes.</param>
    /// <param name="stateWriter">The store role used to persist post-transition envelopes.</param>
    /// <param name="dispatcher">The dispatcher used to execute each leased envelope.</param>
    /// <param name="options">The batch, lease, owner, and retry settings for this processor instance.</param>
    /// <param name="clock">The time provider used for leasing and retry timestamps.</param>
    /// <param name="logger">The optional logger for lease, pass, and dispatch diagnostics.</param>
    public InboxProcessor(
        IInboxLeaseStore leaseStore,
        IInboxStateWriter stateWriter,
        IInboxDispatcher dispatcher,
        InboxProcessorOptions options,
        TimeProvider clock,
        ILogger<InboxProcessor>? logger = null)
        : this(
            new SplitInboxProcessingStore(leaseStore, stateWriter),
            dispatcher,
            options,
            clock,
            null,
            logger)
    {
    }

    /// <summary>
    ///     Adapts separate lease and state writer roles to <see cref="IInboxProcessingStore" />.
    /// </summary>
    private sealed class SplitInboxProcessingStore : IInboxProcessingStore
    {
        /// <summary>
        ///     The store role used to accept new envelopes.
        /// </summary>
        private readonly IInboxStore _store;

        /// <summary>
        ///     The store role used to lease due envelopes.
        /// </summary>
        private readonly IInboxLeaseStore _leaseStore;

        /// <summary>
        ///     The store role used to persist post-transition envelopes.
        /// </summary>
        private readonly IInboxStateWriter _stateWriter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="SplitInboxProcessingStore" /> class.
        /// </summary>
        /// <param name="leaseStore">The store role used to lease due envelopes.</param>
        /// <param name="stateWriter">The store role used to persist post-transition envelopes.</param>
        public SplitInboxProcessingStore(IInboxLeaseStore leaseStore, IInboxStateWriter stateWriter)
        {
            _leaseStore = leaseStore ?? throw new ArgumentNullException(nameof(leaseStore));
            _stateWriter = stateWriter ?? throw new ArgumentNullException(nameof(stateWriter));
            _store = leaseStore as IInboxStore
                ?? stateWriter as IInboxStore
                ?? throw new ArgumentException(
                    "At least one store role must implement IInboxStore.",
                    nameof(leaseStore));
        }

        /// <inheritdoc />
        public Task<InboxEnvelope> AddAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default) =>
            _store.AddAsync(envelope, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxEnvelope>> AddBatchAsync(
            IReadOnlyList<InboxEnvelope> envelopes,
            CancellationToken cancellationToken = default) =>
            _store.AddBatchAsync(envelopes, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<InboxEnvelope>> LeasePendingAsync(
            InboxLeaseRequest request,
            CancellationToken cancellationToken = default) =>
            _leaseStore.LeasePendingAsync(request, cancellationToken);

        /// <inheritdoc />
        public Task<bool> RenewLeaseAsync(
            Guid messageId,
            string leaseOwner,
            DateTimeOffset expiresAt,
            CancellationToken cancellationToken = default) =>
            _leaseStore.RenewLeaseAsync(messageId, leaseOwner, expiresAt, cancellationToken);

        /// <inheritdoc />
        public Task PersistAsync(IReadOnlyList<InboxEnvelope> envelopes, CancellationToken cancellationToken = default) =>
            _stateWriter.PersistAsync(envelopes, cancellationToken);
    }
}
