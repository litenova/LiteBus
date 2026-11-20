using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Adds a provider's staging to an audit chain store for the contract tests.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     Every port operation goes to the wrapped store unchanged; <see cref="StageAsync" /> runs the staging the
///     provider's fixture supplies.
/// </remarks>
public sealed class StagingAuditChainStore<TDocument> : IStagingAuditChainStore<TDocument>
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Gets the store every port operation goes to.
    /// </summary>
    private readonly IAuditChainStore<TDocument> _inner;

    /// <summary>
    ///     Gets the staging that stages a document and commits it.
    /// </summary>
    private readonly Func<string, TDocument, CancellationToken, ValueTask> _stage;

    /// <summary>
    ///     Initializes a new instance of the <see cref="StagingAuditChainStore{TDocument}" /> class.
    /// </summary>
    /// <param name="inner">The store every port operation goes to.</param>
    /// <param name="stage">Stages a document and commits it, the way the provider's audit trail does.</param>
    public StagingAuditChainStore(
        IAuditChainStore<TDocument> inner,
        Func<string, TDocument, CancellationToken, ValueTask> stage)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(stage);

        _inner = inner;
        _stage = stage;
    }

    /// <inheritdoc />
    public ValueTask StageAsync(string segment, TDocument document, CancellationToken cancellationToken)
    {
        return _stage(segment, document, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
    {
        return _inner.ReadHeadAsync(segment, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask AppendAsync(
        string segment,
        AuditSegmentHead expected,
        IReadOnlyList<TDocument> documents,
        AuditSegmentHead updated,
        CancellationToken cancellationToken)
    {
        return _inner.AppendAsync(segment, expected, documents, updated, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TDocument> ListUnlinkedAsync(
        string segment,
        TimeSpan? olderThan,
        CancellationToken cancellationToken)
    {
        return _inner.ListUnlinkedAsync(segment, olderThan, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(TimeSpan? olderThan, CancellationToken cancellationToken)
    {
        return _inner.ListUnlinkedSegmentsAsync(olderThan, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<TDocument> ReadSegmentAsync(
        string segment,
        long fromSequence,
        long? toSequence,
        CancellationToken cancellationToken)
    {
        return _inner.ReadSegmentAsync(segment, fromSequence, toSequence, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
        AuditSegmentFilter filter,
        CancellationToken cancellationToken)
    {
        return _inner.ListHeadsAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        return _inner.StoreCheckpointAsync(checkpoint, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        return _inner.ReadCheckpointAsync(checkpointId, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(CancellationToken cancellationToken)
    {
        return _inner.ListUnpublishedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask MarkPublishedAsync(Guid checkpointId, string? publishError, CancellationToken cancellationToken)
    {
        return _inner.MarkPublishedAsync(checkpointId, publishError, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
    {
        return _inner.EraseAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
    {
        return _inner.ExpireAsync(segment, cancellationToken);
    }
}
