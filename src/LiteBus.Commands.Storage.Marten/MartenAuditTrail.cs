using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Writes audit records as Marten documents, staged on the scoped session for successes and out of band otherwise.
/// </summary>
/// <remarks>
///     <para>
///         A record for a successful mediation is staged on the scoped <see cref="IDocumentSession" /> and committed by
///         the same <c>SaveChangesAsync</c> that commits the handler change, so the record and the change are atomic:
///         both commit together and both roll back together. The ordering that makes this work is fixed by priority.
///         The audit completion handler runs at <see cref="HandlerPriorities.Observability" /> and the unit-of-work
///         commit runs at <see cref="HandlerPriorities.UnitOfWork" />, and handlers run in ascending priority order,
///         so the writer always stages before the commit.
///     </para>
///     <para>
///         A record for a denial, failure, cancellation, or invalid message cannot ride that transaction, because the
///         unit of work rolls the session back on exactly those paths. Those records are written out of band on a
///         short session opened from <see cref="IDocumentStore" /> and saved immediately, so a refusal or a fault is
///         still recorded when the change it describes never commits.
///     </para>
///     <para>
///         Registered with the existing messaging <c>UseAuditTrail</c> API, which constructs this trail from the
///         container with its default scoped lifetime. The application registers Marten itself with <c>AddMarten</c>,
///         which supplies both the scoped session and the document store; LiteBus does not register Marten.
///     </para>
///     <para>
///         When <see cref="MartenAuditTrailOptions" /> enables the hash chain, every record, including successes, is
///         written out of band on its own serialized session instead. The sequence number is assigned under the
///         segment lock at commit time, which a staged session cannot hold: the unit of work commits later, on a
///         session the trail does not control. A chained success record is therefore durable on its own rather than
///         atomic with the handler change, and a rolled-back handler still leaves its audit record behind.
///     </para>
/// </remarks>
public sealed class MartenAuditTrail : IAuditTrail
{
    /// <summary>
    ///     Gets the scoped document session that carries staged success records until the unit of work commits.
    /// </summary>
    private readonly IDocumentSession _session;

    /// <summary>
    ///     Gets the document store that opens short sessions for out-of-band non-success records.
    /// </summary>
    private readonly IDocumentStore _documentStore;

    /// <summary>
    ///     Gets the hash chain, segmenting, and checkpoint configuration.
    /// </summary>
    private readonly MartenAuditTrailOptions _options;

    /// <summary>
    ///     Gets the clock that stamps checkpoint candidates.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Gets the provider that resolves pieces the builder named as types, or <see langword="null" />.
    /// </summary>
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditTrail" /> class.
    /// </summary>
    /// <param name="session">The scoped document session the handler staged its documents on.</param>
    /// <param name="documentStore">The application-owned Marten document store.</param>
    /// <param name="options">
    ///     The hash chain configuration, or <see langword="null" /> to write plain records with no chain.
    /// </param>
    /// <param name="timeProvider">
    ///     The clock that stamps checkpoint candidates, or <see langword="null" /> for
    ///     <see cref="TimeProvider.System" />.
    /// </param>
    /// <param name="serviceProvider">
    ///     Resolves pieces the builder named as types, or <see langword="null" /> when every piece was given as an
    ///     instance.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     <paramref name="options" /> carries a checkpoint scan interval that is zero or negative.
    /// </exception>
    public MartenAuditTrail(
        IDocumentSession session,
        IDocumentStore documentStore,
        MartenAuditTrailOptions? options = null,
        TimeProvider? timeProvider = null,
        IServiceProvider? serviceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(documentStore);

        options ??= new MartenAuditTrailOptions();
        options.Validate();

        _session = session;
        _documentStore = documentStore;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        if (_options.HashChainEnabled)
        {
            await WriteChainedAsync(record, cancellationToken).ConfigureAwait(false);
            return;
        }

        var document = Map(record);

        if (record.Outcome == AuditOutcome.Succeeded)
        {
            // Staged, not saved: the unit-of-work completion handler runs after the audit writer and commits this
            // session, so the record commits in the same SaveChangesAsync as the handler change.
            _session.Store(document);
            return;
        }

        // The unit of work rolls the handler session back on this path, so staging here would lose the record. A
        // separate short session saves it immediately instead.
        var session = _documentStore.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            session.Store(document);
            await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Verifies the hash chain of one segment from its first record to its head.
    /// </summary>
    /// <param name="segment">The segment to verify, as the configured segmenter named it.</param>
    /// <param name="cancellationToken">The token used to cancel the verification read.</param>
    /// <returns>Every break found, the head, and the comparison against the latest checkpoint.</returns>
    /// <remarks>
    ///     Verification recomputes every hash with the scheme each record names and compares the head against the
    ///     latest stored checkpoint, so it proves both internal consistency and coverage: a rewritten suffix fails
    ///     the checkpoint comparison even when its links are consistent.
    /// </remarks>
    public Task<AuditChainVerification> VerifyChainAsync(string segment, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var chain = new MartenAuditChain(_documentStore, _options, _timeProvider, _serviceProvider);

        return chain.VerifyAsync(segment, cancellationToken);
    }

    /// <summary>
    ///     Appends one record to its segment with the next sequence number and a hash over its predecessor.
    /// </summary>
    /// <param name="record">The record produced at the mediation boundary.</param>
    /// <param name="cancellationToken">The token used to cancel the append.</param>
    /// <returns>A task representing the asynchronous append.</returns>
    private Task WriteChainedAsync(AuditRecord record, CancellationToken cancellationToken)
    {
        return _options.RequireChain().AppendAsync(
            _documentStore,
            record,
            _timeProvider,
            _serviceProvider,
            cancellationToken);
    }

    /// <summary>
    ///     Flattens one audit record into its Marten document.
    /// </summary>
    /// <param name="record">The record produced at the mediation boundary.</param>
    /// <returns>The document staged or written for the record.</returns>
    private static MartenAuditRecord Map(AuditRecord record)
    {
        return new MartenAuditRecordMapper().Map(record);
    }
}
