using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.Recording;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Writes audit records as PostgreSQL rows, staged in the business transaction for successes and out of band
///     otherwise.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         A record for a successful mediation is staged in the transaction the scope's <see cref="IPostgreSqlTransactionProvider" />
///         reports when one is active, so the record commits or rolls back with the handler change. The chain linker assigns the staged record
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
///         <see cref="IPostgreSqlTransactionProvider" /> when its business data lives on the same
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
    ///     Gets the dispatch-scope provider the record mapper resolves services from when no mediation context is
    ///     current, or <see langword="null" />.
    /// </summary>
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditTrail{TDocument}" /> class.
    /// </summary>
    /// <param name="store">The chain store that stages documents.</param>
    /// <param name="mapper">The mapper from an audit record to a new stored document.</param>
    /// <param name="segmenter">The segmenter that assigns a stored document to its segment.</param>
    /// <param name="serviceProvider">
    ///     The dispatch-scope provider the staged-segment holder and the business transaction provider are resolved
    ///     from, or <see langword="null" /> when the composition links nothing after commit and stages outside any
    ///     business transaction.
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
        _serviceProvider = serviceProvider;
        _stagedSegments = serviceProvider?.GetService(typeof(AuditStagedSegments)) as AuditStagedSegments;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The mapper runs through <see cref="IAuditRecordMapper{TDocument}.MapAsync" /> on the connection and
    ///     transaction the record is staged on, offered as <see cref="NpgsqlConnection" /> and
    ///     <see cref="NpgsqlTransaction" /> features, so a row the mapper writes commits or rolls back with the record.
    /// </remarks>
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var mapping = AuditMappingScope.Resolve(record, _serviceProvider);
        var succeeded = record.Outcome == AuditOutcome.Succeeded;

        // A success joins the business transaction the scope's transaction provider reports, so it commits or rolls
        // back with the handler change. Any other outcome is rolled back by the unit of work, so it is written on its own
        // connection and transaction and committed immediately.
        var ambient = succeeded
            ? _serviceProvider?.GetService(typeof(IPostgreSqlTransactionProvider)) as IPostgreSqlTransactionProvider
            : null;
        var (_, segment) = await _store.MapAndStageAsync(
            ambient,
            (connection, transaction) =>
            {
                mapping.Features.Set(connection);

                if (transaction is not null)
                {
                    mapping.Features.Set(transaction);
                }

                return _mapper.MapAsync(record, mapping, cancellationToken);
            },
            document => SegmentOf(record, document),
            cancellationToken).ConfigureAwait(false);

        if (succeeded)
        {
            _stagedSegments?.Add(segment);
        }
    }

    /// <summary>
    ///     Gets the segment of a mapped document.
    /// </summary>
    /// <param name="record">The record the document was mapped from, named in the error.</param>
    /// <param name="document">The mapped document.</param>
    /// <returns>The segment.</returns>
    /// <exception cref="InvalidOperationException">The segmenter returned no segment.</exception>
    private string SegmentOf(AuditRecord record, TDocument document)
    {
        var segment = _segmenter.SegmentOf(document);

        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new InvalidOperationException(
                $"The audit segmenter returned no segment for action '{record.Action}'. " +
                "Return a stable segment name from the configured segmenter.");
        }

        return segment;
    }
}
