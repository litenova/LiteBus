using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Engine;

namespace LiteBus.Auditing.Segments;

/// <summary>
///     Lists, expires, and erases audit chain segments through the configured chain store.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         Listing maps stored heads to summaries without parsing segment keys: the tenant, attributes, and opened-at
///         stored on each head are what the filter selects on.
///     </para>
///     <para>
///         Expiry is retention: the lifecycle cuts a final checkpoint, pending checkpoints are published so the proof
///         is anchored before the records go, and only then are the segment documents deleted and the head marked
///         expired. The head and the stored checkpoints are kept. Expiring a segment that holds no records, or one
///         that has expired already, throws.
///     </para>
///     <para>
///         Erasure is removal on request: it deletes the records, heads, and stored checkpoints matching the filter
///         and publishes nothing by default. When the chain turns erasure notices on, every registered
///         <see cref="IErasureNoticePublisher" /> receives one notice per erased segment after the deletion; the
///         notice carries no hash.
///     </para>
/// </remarks>
internal sealed class AuditSegmentManager<TDocument> : IAuditSegmentManager
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Gets the store the segment reads, expiry, and erasure run against.
    /// </summary>
    private readonly IAuditChainStore<TDocument> _store;

    /// <summary>
    ///     Gets the lifecycle that cuts the final checkpoint expiry anchors.
    /// </summary>
    private readonly ICheckpointLifecycle _checkpoints;

    /// <summary>
    ///     Gets the chain configuration, which decides whether erasure publishes notices.
    /// </summary>
    private readonly AuditHashChainOptions _chainOptions;

    /// <summary>
    ///     Gets the clock that stamps erasure notices.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Gets the provider that resolves erasure notice publishers, or <see langword="null" /> when erasure
    ///     notices are off and no publisher is needed.
    /// </summary>
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    ///     Gets the service type erasure notice publishers are resolved as: every registered publisher.
    /// </summary>
    private static readonly Type NoticePublisherSequenceType = typeof(IEnumerable<IErasureNoticePublisher>);

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditSegmentManager{TDocument}" /> class.
    /// </summary>
    /// <param name="store">The store the segment reads, expiry, and erasure run against.</param>
    /// <param name="checkpoints">The lifecycle that cuts the final checkpoint expiry anchors.</param>
    /// <param name="chainOptions">The chain configuration, which decides whether erasure publishes notices.</param>
    /// <param name="timeProvider">The clock that stamps erasure notices.</param>
    /// <param name="serviceProvider">
    ///     Resolves erasure notice publishers when notices are on, or <see langword="null" /> when notices are off.
    /// </param>
    internal AuditSegmentManager(
        IAuditChainStore<TDocument> store,
        ICheckpointLifecycle checkpoints,
        AuditHashChainOptions chainOptions,
        TimeProvider timeProvider,
        IServiceProvider? serviceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(chainOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _checkpoints = checkpoints;
        _chainOptions = chainOptions;
        _timeProvider = timeProvider;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AuditSegmentSummary> ListAsync(
        AuditSegmentFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        await foreach (var head in _store.ListHeadsAsync(filter, cancellationToken).ConfigureAwait(false))
        {
            yield return new AuditSegmentSummary(
                head.Info.Key,
                head.Info.TenantId,
                head.Info.Attributes,
                head.Count,
                head.LastSequence == 0 ? null : head.LastSequence,
                head.LastCheckpointSequence,
                head.Expired);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The final checkpoint is cut and stored before anything is published or deleted, so a failed expiry keeps
    ///     every record. Expiry is safe to retry: a second call on an expired segment returns the stored final
    ///     checkpoint, and a call after a failed attempt reuses the final checkpoint that attempt stored. Publishing flushes every pending checkpoint rather than only the final one, because pending
    ///     checkpoints are published as one pass.
    /// </remarks>
    public async ValueTask<AuditCheckpoint> ExpireAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var head = await _store.ReadHeadAsync(segment, cancellationToken).ConfigureAwait(false);

        if (head is null || head.LastSequence == 0)
        {
            throw new InvalidOperationException(
                $"The audit segment '{segment}' holds no records, so it cannot expire.");
        }

        // A retried expiry finds the final checkpoint an earlier attempt stored by its derived identifier, so a
        // retention job that stopped after the cut, or after the head was marked expired, completes instead of failing.
        var stored = await _store.ReadCheckpointAsync(FinalCheckpointId.For(segment), cancellationToken)
            .ConfigureAwait(false);

        if (head.Expired)
        {
            return stored ?? throw new InvalidOperationException(
                $"The audit segment '{segment}' has expired, but its final checkpoint is not stored, so it cannot be "
                + "returned. The segment was expired before final checkpoints had a derived identifier.");
        }

        var checkpoint = stored ?? await _checkpoints
            .CutAsync(segment, null, AuditCheckpointKind.Final, cancellationToken)
            .ConfigureAwait(false);

        await _checkpoints.PublishPendingAsync(cancellationToken).ConfigureAwait(false);
        await _store.ExpireAsync(segment, cancellationToken).ConfigureAwait(false);

        return checkpoint;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The matching heads are listed before the deletion, so the returned count and the erasure notices describe
    ///     the segments the filter matched. Notices go out after the deletion, so a notice always reports an erasure
    ///     that happened; a publisher that throws fails the erasure loudly instead of dropping the notice silently.
    /// </remarks>
    public async ValueTask<int> EraseAsync(
        AuditSegmentFilter filter,
        string reason,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        List<AuditSegmentHead> heads = [];

        await foreach (var head in _store.ListHeadsAsync(filter, cancellationToken).ConfigureAwait(false))
        {
            heads.Add(head);
        }

        if (heads.Count == 0)
        {
            return 0;
        }

        var publishers = ResolveNoticePublishers();

        await _store.EraseAsync(filter, cancellationToken).ConfigureAwait(false);

        if (publishers.Count == 0)
        {
            return heads.Count;
        }

        var erasedAt = _timeProvider.GetUtcNow();

        foreach (var head in heads)
        {
            var notice = new AuditErasureNotice(head.Info.Key, head.Count, reason, erasedAt);

            foreach (var publisher in publishers)
            {
                await publisher.PublishAsync(notice, cancellationToken).ConfigureAwait(false);
            }
        }

        return heads.Count;
    }

    /// <summary>
    ///     Resolves the publishers erasure notices go to, or an empty list when notices are off.
    /// </summary>
    /// <returns>The registered notice publishers, or an empty list when notices are off.</returns>
    /// <remarks>
    ///     Notices that are on without a publisher fail before anything is deleted: a product that must prove an
    ///     erasure happened must learn that its proof has nowhere to go while the data is still there.
    /// </remarks>
    private List<IErasureNoticePublisher> ResolveNoticePublishers()
    {
        if (!_chainOptions.PublishErasureNotices)
        {
            return [];
        }

        if (_serviceProvider is null)
        {
            throw new InvalidOperationException(
                "Erasure notices are on but no service provider is available. Pass an IServiceProvider when " +
                "constructing the segment manager so erasure notices can be published.");
        }

        var resolved = (IEnumerable<IErasureNoticePublisher>?) _serviceProvider.GetService(NoticePublisherSequenceType);
        List<IErasureNoticePublisher> publishers = resolved is null ? [] : [.. resolved];

        if (publishers.Count == 0)
        {
            throw new InvalidOperationException(
                "Erasure notices are on but no erasure notice publisher is registered. Register an " +
                "IErasureNoticePublisher in the container or turn erasure notices off.");
        }

        return publishers;
    }
}
