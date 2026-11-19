using System;
using LiteBus.Auditing.Abstractions;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Engine;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Linking;

/// <summary>
///     Links staged audit records into their hash chain segments in commit order.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     The <see cref="AuditLink.AfterCommit" /> path stages records in the business transaction and links
///     them afterwards: <see cref="AuditLinkCommitListener" /> calls <see cref="LinkPendingAsync" /> for
///     each staged segment right after the unit of work commits, and
///     <see cref="AuditLinkSweepBackgroundService" /> calls <see cref="SweepAsync" /> on an interval for
///     the records a crash left behind. Positions come from
///     <see cref="AuditChainHeadWriter{TDocument}" />, so inline linking and the sweep share the head
///     contention discipline of the <see cref="AuditLink.InTransaction" /> and
///     <see cref="AuditLink.AtWrite" /> paths.
/// </remarks>
internal sealed class AuditLinker<TDocument> : IAuditLinker
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     The durable chain operations unlinked segments and heads are listed from. Held for the life of
    ///     the linker.
    /// </summary>
    private readonly IAuditChainStore<TDocument> _store;

    /// <summary>
    ///     The writer that assigns positions and appends under head contention.
    ///     Held for the life of the linker.
    /// </summary>
    private readonly AuditChainHeadWriter<TDocument> _headWriter;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditLinker{TDocument}" /> class.
    /// </summary>
    /// <param name="store">The durable chain operations the linking runs against.</param>
    /// <param name="segmenter">The segmenter that opens new segments.</param>
    /// <param name="schemes">The registry that resolves the write scheme by name.</param>
    /// <param name="chainOptions">The chain configuration carrying the write scheme name.</param>
    internal AuditLinker(
        IAuditChainStore<TDocument> store,
        IAuditSegmenter<TDocument> segmenter,
        IAuditHashSchemeRegistry schemes,
        AuditHashChainOptions chainOptions)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(segmenter);
        ArgumentNullException.ThrowIfNull(schemes);
        ArgumentNullException.ThrowIfNull(chainOptions);

        _store = store;
        _headWriter = new AuditChainHeadWriter<TDocument>(store, segmenter, schemes, chainOptions);
    }

    /// <inheritdoc />
    public async ValueTask LinkPendingAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        await _headWriter.LinkStagedAsync(segment, null, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Segments are discovered through the unlinked listing rather than the stored heads, because staging
    ///     creates no head: a segment whose records are all staged is found with no head at all, and linking
    ///     it starts the head at version zero. Expired segments are skipped, because expiry deletes their
    ///     staged records and linking them would fail. A segment failure aborts the sweep and the background
    ///     service retries it on the next interval, which keeps a poisoned segment visible instead of
    ///     skipping past it silently.
    /// </remarks>
    public async ValueTask<int> SweepAsync(TimeSpan olderThan, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(olderThan, TimeSpan.Zero);

        var linked = 0;

        await foreach (var segment in _store.ListUnlinkedSegmentsAsync(olderThan, cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var head = await _store.ReadHeadAsync(segment, cancellationToken).ConfigureAwait(false);

            if (head is { Expired: true })
            {
                continue;
            }

            linked += await _headWriter
                .LinkStagedAsync(segment, olderThan, cancellationToken)
                .ConfigureAwait(false);
        }

        return linked;
    }
}
