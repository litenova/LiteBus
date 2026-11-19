using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Linking;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Writes audit records as PostgreSQL rows, staged in the business transaction for successes and out of band
///     otherwise.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         A record for a successful mediation is staged through the ambient <see cref="LiteBus.Storage.PostgreSql.IPostgreSqlTransactionProvider" /> transaction when one is active,
///         so the record commits or rolls back with the handler change. The chain linker assigns the staged record
///         its position after commit, in commit order.
///     </para>
///     <para>
///         A record for a denial, failure, cancellation, or invalid message cannot ride that transaction, because
///         the unit of work rolls it back on exactly those paths. Those records are staged out of band on their own
///         connection and committed immediately, so a refusal or a fault is still recorded when the change it
///         describes never commits, and the linker still positions them.
///     </para>
///     <para>
///         Registered through <c>AuditingModuleBuilder.UsePostgreSqlStore</c>, which records this trail as the open
///         generic the auditing module closes over the configured document type. The application registers an
///         <see cref="LiteBus.Storage.PostgreSql.IPostgreSqlTransactionProvider" /> when its business data lives on the same
///         PostgreSQL transaction; without one, every record commits on its own connection.
///     </para>
/// </remarks>
public sealed class PostgreSqlAuditTrail<TDocument> : IAuditTrail
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Gets the chain store that stages documents in the business transaction or out of band.
    /// </summary>
    private readonly PostgreSqlAuditChainStore<TDocument> _store;

    /// <summary>
    ///     Gets the mapper from an audit record to a new stored document.
    /// </summary>
    private readonly IAuditRecordMapper<TDocument> _mapper;

    /// <summary>
    ///     Gets the segmenter that assigns a stored document to its segment.
    /// </summary>
    private readonly IAuditSegmenter<TDocument> _segmenter;

    /// <summary>
    ///     Gets the holder collecting staged segments for linking after commit, or <see langword="null" />
    ///     when the composition registered none.
    /// </summary>
    private readonly AuditStagedSegments? _stagedSegments;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditTrail{TDocument}" /> class.
    /// </summary>
    /// <param name="store">The chain store that stages documents.</param>
    /// <param name="mapper">The mapper from an audit record to a new stored document.</param>
    /// <param name="segmenter">The segmenter that assigns a stored document to its segment.</param>
    /// <param name="serviceProvider">
    ///     The dispatch-scope provider the staged-segment holder is resolved from, or <see langword="null" />
    ///     when the composition links nothing after commit.
    /// </param>
    public PostgreSqlAuditTrail(
        PostgreSqlAuditChainStore<TDocument> store,
        IAuditRecordMapper<TDocument> mapper,
        IAuditSegmenter<TDocument> segmenter,
        IServiceProvider? serviceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(segmenter);

        _store = store;
        _mapper = mapper;
        _segmenter = segmenter;
        _stagedSegments = serviceProvider?.GetService(typeof(AuditStagedSegments)) as AuditStagedSegments;
    }

    /// <inheritdoc />
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var document = _mapper.Map(record);
        var segment = _segmenter.SegmentOf(document);

        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new InvalidOperationException(
                $"The audit segmenter returned no segment for action '{record.Action}'. " +
                "Return a stable segment name from the configured segmenter.");
        }

        if (record.Outcome == AuditOutcome.Succeeded)
        {
            // Staged, not saved: the record joins the ambient business transaction when one is active, so it
            // commits or rolls back with the handler change.
            await _store.StageAsync(segment, document, cancellationToken).ConfigureAwait(false);
            _stagedSegments?.Add(segment);
            return;
        }

        // The unit of work rolls the business transaction back on this path, so staging there would lose the
        // record. Its own connection commits it immediately instead.
        await _store.StageOutOfBandAsync(segment, document, cancellationToken).ConfigureAwait(false);
    }
}
