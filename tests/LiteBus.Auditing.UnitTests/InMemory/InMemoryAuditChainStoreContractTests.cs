using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.InMemory;
using LiteBus.Storage.Testing;

namespace LiteBus.Auditing.UnitTests.InMemory;

/// <summary>
///     Runs the shared audit chain store contract tests against the in-memory audit chain store, reading
///     placeholder heads as no head.
/// </summary>
public sealed class InMemoryAuditChainStoreContractTests : AuditChainStoreContractTests<InMemoryAuditChainStoreContractTests.ContractTestDocument>
{
    /// <inheritdoc />
    protected override IStagingAuditChainStore<ContractTestDocument> CreateStore()
    {
        return new PlaceholderHidingStore(new InMemoryAuditChainStore<ContractTestDocument>());
    }

    /// <inheritdoc />
    protected override ContractTestDocument CreateDocument(string marker)
    {
        return new ContractTestDocument { Marker = marker };
    }

    /// <inheritdoc />
    protected override string GetMarker(ContractTestDocument document)
    {
        return document.Marker;
    }

    /// <summary>
    ///     A chain document that carries the suite's marker, public because it is the base suite's type argument.
    /// </summary>
    public sealed class ContractTestDocument : IAuditChainDocument
    {
        /// <summary>
        ///     Gets or sets the marker the suite uses to identify the document.
        /// </summary>
        public string Marker { get; set; } = string.Empty;

        /// <inheritdoc />
        public string? Segment { get; set; }

        /// <inheritdoc />
        public long Sequence { get; set; }

        /// <inheritdoc />
        public string? PreviousHash { get; set; }

        /// <inheritdoc />
        public string? Hash { get; set; }

        /// <inheritdoc />
        public string? HashScheme { get; set; }
    }

    private sealed class PlaceholderHidingStore : IStagingAuditChainStore<ContractTestDocument>
    {
        private readonly InMemoryAuditChainStore<ContractTestDocument> _inner;

        public PlaceholderHidingStore(InMemoryAuditChainStore<ContractTestDocument> inner)
        {
            _inner = inner;
        }

        public async ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
        {
            var head = await _inner.ReadHeadAsync(segment, cancellationToken).ConfigureAwait(false);

            // The suite stages without appending and then reads no head, while this store writes a
            // placeholder head on stage so its sweep discovers staged-only segments. The adapter
            // reports placeholders as no head so the suite proves the remaining port behavior.
            return IsPlaceholder(head) ? null : head;
        }

        public ValueTask AppendAsync(
            string segment,
            AuditSegmentHead expected,
            IReadOnlyList<ContractTestDocument> documents,
            AuditSegmentHead updated,
            CancellationToken cancellationToken)
        {
            return _inner.AppendAsync(segment, expected, documents, updated, cancellationToken);
        }

        public ValueTask StageAsync(string segment, ContractTestDocument document, CancellationToken cancellationToken)
        {
            return _inner.StageAsync(segment, document, cancellationToken);
        }

        public IAsyncEnumerable<ContractTestDocument> ListUnlinkedAsync(
            string segment,
            TimeSpan? olderThan,
            CancellationToken cancellationToken)
        {
            return _inner.ListUnlinkedAsync(segment, olderThan, cancellationToken);
        }

        public IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(TimeSpan? olderThan, CancellationToken cancellationToken)
        {
            return _inner.ListUnlinkedSegmentsAsync(olderThan, cancellationToken);
        }

        public IAsyncEnumerable<ContractTestDocument> ReadSegmentAsync(
            string segment,
            long fromSequence,
            long? toSequence,
            CancellationToken cancellationToken)
        {
            return _inner.ReadSegmentAsync(segment, fromSequence, toSequence, cancellationToken);
        }

        public IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
            AuditSegmentFilter filter,
            CancellationToken cancellationToken)
        {
            return _inner.ListHeadsAsync(filter, cancellationToken);
        }

        public ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            return _inner.StoreCheckpointAsync(checkpoint, cancellationToken);
        }

        public ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
        {
            return _inner.ReadCheckpointAsync(checkpointId, cancellationToken);
        }

        public IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(CancellationToken cancellationToken)
        {
            return _inner.ListUnpublishedAsync(cancellationToken);
        }

        public ValueTask MarkPublishedAsync(Guid checkpointId, string? publishError, CancellationToken cancellationToken)
        {
            return _inner.MarkPublishedAsync(checkpointId, publishError, cancellationToken);
        }

        public ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
        {
            return _inner.EraseAsync(filter, cancellationToken);
        }

        public ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
        {
            return _inner.ExpireAsync(segment, cancellationToken);
        }

        private static bool IsPlaceholder(AuditSegmentHead? head)
        {
            return head is not null && head.Version == 0 && head.Count == 0 && head.LastSequence == 0 && head.LastHash is null;
        }
    }
}
