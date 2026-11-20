using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Linking;
using LiteBus.Auditing.Recording;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Writes audit records as Entity Framework Core documents, staged on the scoped context for successes
///     and out of band otherwise.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         A record for a successful mediation is staged on the scoped context and committed by the same
///         <c>SaveChanges</c> that commits the handler change, so the record and the change are atomic: both
///         commit together and both roll back together. The chain linker assigns the staged record its position
///         after commit, in commit order.
///     </para>
///     <para>
///         A record for a denial, failure, cancellation, or invalid message cannot ride that transaction,
///         because the unit of work rolls the scoped context back on exactly those paths. Those records are
///         staged out of band on an isolated context and saved immediately, so a refusal or a fault is still
///         recorded when the change it describes never commits, and the linker still positions them. Register
///         <see cref="EfCoreAuditDbContextFactory{TContext}" /> for the application database context so the
///         isolated contexts can be opened.
///     </para>
///     <para>
///         Registered through <c>AuditingModuleBuilder.UseEntityFrameworkCoreStore</c>, which records this trail
///         as the open generic the auditing module closes over the configured document type. The application
///         registers its own database context implementing <see cref="IAuditDbContext" />; LiteBus does not
///         register it.
///     </para>
/// </remarks>
public sealed class EfCoreAuditTrail<TDocument> : IAuditTrail
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Gets the chain store that stages documents on the scoped context or out of band.
    /// </summary>
    private readonly EfCoreAuditChainStore<TDocument> _store;

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
    ///     Initializes a new instance of the <see cref="EfCoreAuditTrail{TDocument}" /> class.
    /// </summary>
    /// <param name="store">The chain store that stages documents.</param>
    /// <param name="mapper">The mapper from an audit record to a new stored document.</param>
    /// <param name="segmenter">The segmenter that assigns a stored document to its segment.</param>
    /// <param name="serviceProvider">
    ///     The dispatch-scope provider the staged-segment holder is resolved from, or <see langword="null" />
    ///     when the composition links nothing after commit.
    /// </param>
    public EfCoreAuditTrail(
        EfCoreAuditChainStore<TDocument> store,
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
    ///     The mapper runs through <see cref="IAuditRecordMapper{TDocument}.MapAsync" /> with the context the record is
    ///     written on, offered as the <see cref="DbContext" /> feature, so an entity the mapper adds commits or rolls
    ///     back with the record.
    /// </remarks>
    public async Task WriteAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var mapping = AuditMappingScope.Resolve(record, _serviceProvider);

        if (record.Outcome == AuditOutcome.Succeeded)
        {
            // Staged, not saved: the unit-of-work completion handler runs after the audit writer and commits the
            // scoped context, so the record commits in the same SaveChanges as the handler change.
            mapping.Features.Set(_store.BusinessContext);

            var document = await _mapper.MapAsync(record, mapping, cancellationToken).ConfigureAwait(false);
            var segment = SegmentOf(record, document);

            await _store.StageAsync(segment, document, cancellationToken).ConfigureAwait(false);
            _stagedSegments?.Add(segment);
            return;
        }

        // The unit of work rolls the scoped context back on this path, so staging there would lose the record.
        // An isolated context saves it immediately instead, with whatever the mapper added to it.
        await _store.StageOutOfBandCoreAsync(
            context =>
            {
                mapping.Features.Set(context);
                return _mapper.MapAsync(record, mapping, cancellationToken);
            },
            document => SegmentOf(record, document),
            cancellationToken).ConfigureAwait(false);
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
