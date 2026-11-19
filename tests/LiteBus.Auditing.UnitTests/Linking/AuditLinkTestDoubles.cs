using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing;
using LiteBus.Auditing.Engine;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Audit;
using LiteBus.Auditing.Hashing;

namespace LiteBus.Auditing.UnitTests.Linking;

/// <summary>
///     A stored audit document with one business field, standing in for an application-owned document type.
/// </summary>
internal sealed class LinkTestDocument : IAuditChainDocument
{
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

    /// <summary>
    ///     Gets or sets the business action recorded, the one tamper-evident field beyond the chain position.
    /// </summary>
    public string Action { get; set; } = string.Empty;
}

/// <summary>
///     The canonical form of <see cref="LinkTestDocument" />: every field except the hash itself.
/// </summary>
internal sealed class LinkTestCanonicalizer : IAuditCanonicalizer<LinkTestDocument>
{
    /// <inheritdoc />
    public byte[] Canonicalize(LinkTestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Encoding.UTF8.GetBytes(string.Concat(
            document.Segment,
            "\n",
            document.Sequence.ToString(CultureInfo.InvariantCulture),
            "\n",
            document.PreviousHash,
            "\n",
            document.Action));
    }
}

/// <summary>
///     Assigns every document to one fixed segment, with an optional genesis document.
/// </summary>
internal sealed class LinkTestSegmenter : IAuditSegmenter<LinkTestDocument>
{
    /// <summary>
    ///     The segment every document is assigned to.
    /// </summary>
    private readonly string _segment;

    /// <summary>
    ///     Whether new segments open with a genesis document.
    /// </summary>
    private readonly bool _withGenesis;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LinkTestSegmenter" /> class.
    /// </summary>
    /// <param name="segment">The segment every document is assigned to.</param>
    /// <param name="withGenesis">Whether new segments open with a genesis document.</param>
    public LinkTestSegmenter(string segment, bool withGenesis = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        _segment = segment;
        _withGenesis = withGenesis;
    }

    /// <inheritdoc />
    public string SegmentOf(LinkTestDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return _segment;
    }

    /// <inheritdoc />
    public LinkTestDocument? CreateGenesis(string segment, LinkTestDocument first)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(first);

        if (!_withGenesis)
        {
            return null;
        }

        return new LinkTestDocument { Action = "genesis" };
    }

    /// <inheritdoc />
    public AuditSegmentInfo DescribeSegment(string segment, LinkTestDocument first)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(first);

        return new AuditSegmentInfo(
            segment,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            DateTimeOffset.UtcNow);
    }
}

/// <summary>
///     Resolves one fixed hash scheme for the link test document type.
/// </summary>
internal sealed class LinkTestSchemeRegistry : IAuditHashSchemeRegistry
{
    /// <summary>
    ///     The scheme every resolve returns.
    /// </summary>
    private readonly AuditHashScheme<LinkTestDocument> _scheme;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LinkTestSchemeRegistry" /> class.
    /// </summary>
    /// <param name="scheme">The scheme every resolve returns.</param>
    public LinkTestSchemeRegistry(AuditHashScheme<LinkTestDocument> scheme)
    {
        ArgumentNullException.ThrowIfNull(scheme);

        _scheme = scheme;
    }

    /// <inheritdoc />
    public AuditHashScheme<TDocument> Resolve<TDocument>(string schemeName)
        where TDocument : class, IAuditChainDocument
    {
        if (!string.Equals(schemeName, _scheme.Name, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"The hash scheme '{schemeName}' is not registered.");
        }

        return (AuditHashScheme<TDocument>) (object) _scheme;
    }
}

/// <summary>
///     Builds link test schemes and chain options over the test canonical form.
/// </summary>
internal static class LinkTestData
{
    /// <summary>
    ///     Builds a hash scheme over the test canonical form and the default link rule.
    /// </summary>
    /// <param name="name">The scheme name stored on documents.</param>
    /// <returns>The scheme.</returns>
    public static AuditHashScheme<LinkTestDocument> TestScheme(string name = "sha256-v2")
    {
        return new AuditHashScheme<LinkTestDocument>(
            name,
            new LinkTestCanonicalizer(),
            new PreviousHashLinkRule(),
            new Sha256AuditHasher());
    }

    /// <summary>
    ///     Builds chain options writing with the given scheme name.
    /// </summary>
    /// <param name="schemeName">The scheme new records are written with.</param>
    /// <returns>The chain options.</returns>
    public static AuditHashChainOptions ChainOptions(string schemeName = "sha256-v2")
    {
        return new AuditHashChainOptions { SchemeName = schemeName };
    }
}

/// <summary>
///     A clock with a settable instant for age-gated sweep tests.
/// </summary>
internal sealed class LinkTestTimeProvider : TimeProvider
{
    /// <summary>
    ///     The instant the clock reports.
    /// </summary>
    private DateTimeOffset _utcNow;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LinkTestTimeProvider" /> class.
    /// </summary>
    /// <param name="utcNow">The instant the clock reports.</param>
    public LinkTestTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    /// <inheritdoc />
    public override DateTimeOffset GetUtcNow()
    {
        return _utcNow;
    }

    /// <summary>
    ///     Moves the clock to a new instant.
    /// </summary>
    /// <param name="utcNow">The instant the clock reports from now on.</param>
    public void SetUtcNow(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    /// <summary>
    ///     Moves the clock forward by the given span.
    /// </summary>
    /// <param name="span">How far the clock moves forward.</param>
    public void Advance(TimeSpan span)
    {
        _utcNow += span;
    }
}

/// <summary>
///     An in-memory audit chain store with head version checks and commit-ordered staging.
/// </summary>
/// <remarks>
///     Staging creates no head, mirroring the durable stores: a staged-only segment has no head until the
///     first append creates it, and the sweep discovers it through the unlinked listing. Appends assert the
///     expected head version and reject duplicate sequences, mirroring the conditional write and unique
///     backstop of the durable stores.
/// </remarks>
internal sealed class LinkTestStore : IAuditChainStore<LinkTestDocument>
{
    /// <summary>
    ///     Guards every collection below.
    /// </summary>
    private readonly object _lock = new();

    /// <summary>
    ///     The heads by segment key.
    /// </summary>
    private readonly Dictionary<string, AuditSegmentHead> _heads = new(StringComparer.Ordinal);

    /// <summary>
    ///     The linked documents by segment key, in ascending sequence order.
    /// </summary>
    private readonly Dictionary<string, List<LinkTestDocument>> _linked = new(StringComparer.Ordinal);

    /// <summary>
    ///     The staged documents by segment key, in commit order.
    /// </summary>
    private readonly Dictionary<string, List<StagedRecord>> _unlinked = new(StringComparer.Ordinal);

    /// <summary>
    ///     The stored checkpoints by checkpoint id.
    /// </summary>
    private readonly Dictionary<Guid, AuditCheckpoint> _checkpoints = [];

    /// <summary>
    ///     The clock that stamps staged records.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The commit order counter staging increments per record.
    /// </summary>
    private long _commitSequence;

    /// <summary>
    ///     Initializes a new instance of the <see cref="LinkTestStore" /> class.
    /// </summary>
    /// <param name="timeProvider">The clock that stamps staged records, or <see langword="null" /> for the system clock.</param>
    public LinkTestStore(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public ValueTask<AuditSegmentHead?> ReadHeadAsync(string segment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _heads.TryGetValue(segment, out var head);

            return ValueTask.FromResult(head);
        }
    }

    /// <inheritdoc />
    public ValueTask AppendAsync(
        string segment,
        AuditSegmentHead expected,
        IReadOnlyList<LinkTestDocument> documents,
        AuditSegmentHead updated,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(documents);
        ArgumentNullException.ThrowIfNull(updated);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _heads.TryGetValue(segment, out var current);

            if ((current?.Version ?? 0) != expected.Version)
            {
                throw new AuditHeadConflictException(
                    $"The audit head for segment '{segment}' changed between read and write.");
            }

            if (!_linked.TryGetValue(segment, out var linked))
            {
                linked = [];
                _linked[segment] = linked;
            }

            foreach (var document in documents)
            {
                foreach (var existing in linked)
                {
                    if (existing.Sequence == document.Sequence)
                    {
                        throw new AuditHeadConflictException(
                            $"The audit head for segment '{segment}' changed between read and write.");
                    }
                }
            }

            if (!_unlinked.TryGetValue(segment, out var staged))
            {
                staged = [];
                _unlinked[segment] = staged;
            }

            foreach (var document in documents)
            {
                for (var index = staged.Count - 1; index >= 0; index--)
                {
                    if (ReferenceEquals(staged[index].Document, document))
                    {
                        staged.RemoveAt(index);
                    }
                }

                linked.Add(document);
            }

            linked.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
            _heads[segment] = updated;

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public ValueTask StageAsync(string segment, LinkTestDocument document, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_unlinked.TryGetValue(segment, out var staged))
            {
                staged = [];
                _unlinked[segment] = staged;
            }

            _commitSequence++;
            staged.Add(new StagedRecord(document, _timeProvider.GetUtcNow(), _commitSequence));

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LinkTestDocument> ListUnlinkedAsync(
        string segment,
        TimeSpan? olderThan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        List<StagedRecord> snapshot;

        lock (_lock)
        {
            snapshot = _unlinked.TryGetValue(segment, out var staged) ? [.. staged] : [];
        }

        if (snapshot.Count == 0)
        {
            yield break;
        }

        snapshot.Sort(static (left, right) => left.CommitOrder.CompareTo(right.CommitOrder));

        var now = _timeProvider.GetUtcNow();

        foreach (var record in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (olderThan.HasValue && now - record.StagedAt <= olderThan.Value)
            {
                continue;
            }

            yield return record.Document;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<string> ListUnlinkedSegmentsAsync(
        TimeSpan? olderThan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        List<string> snapshot;

        lock (_lock)
        {
            var now = _timeProvider.GetUtcNow();

            snapshot = _unlinked
                .Where(entry => entry.Value
                    .Any(record => !olderThan.HasValue || now - record.StagedAt > olderThan.Value))
                .Select(entry => entry.Key)
                .OrderBy(segment => segment, StringComparer.Ordinal)
                .ToList();
        }

        foreach (var segment in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return segment;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<LinkTestDocument> ReadSegmentAsync(
        string segment,
        long fromSequence,
        long? toSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        List<LinkTestDocument> snapshot;

        lock (_lock)
        {
            snapshot = _linked.TryGetValue(segment, out var linked) ? [.. linked] : [];
        }

        if (snapshot.Count == 0)
        {
            yield break;
        }

        foreach (var document in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (document.Sequence < fromSequence)
            {
                continue;
            }

            if (toSequence.HasValue && document.Sequence > toSequence.Value)
            {
                continue;
            }

            yield return document;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AuditSegmentHead> ListHeadsAsync(
        AuditSegmentFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        List<AuditSegmentHead> snapshot;

        lock (_lock)
        {
            snapshot = [.. _heads.Values];
        }

        foreach (var head in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!MatchesFilter(head, filter))
            {
                continue;
            }

            yield return head;
        }
    }

    /// <inheritdoc />
    public ValueTask StoreCheckpointAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _checkpoints[checkpoint.Id] = checkpoint;

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            _checkpoints.TryGetValue(checkpointId, out var checkpoint);

            return ValueTask.FromResult(checkpoint);
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        List<AuditCheckpoint> snapshot;

        lock (_lock)
        {
            snapshot = [.. _checkpoints.Values];
        }

        foreach (var checkpoint in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (checkpoint.PublishState != CheckpointPublishState.Pending)
            {
                continue;
            }

            yield return checkpoint;
        }
    }

    /// <inheritdoc />
    public ValueTask MarkPublishedAsync(Guid checkpointId, string? publishError, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (_checkpoints.TryGetValue(checkpointId, out var checkpoint))
            {
                _checkpoints[checkpointId] = publishError is null
                    ? checkpoint with
                    {
                        PublishState = CheckpointPublishState.Published,
                        PublishAttempts = checkpoint.PublishAttempts + 1,
                        LastPublishError = null,
                    }
                    : checkpoint with
                    {
                        PublishState = CheckpointPublishState.Pending,
                        PublishAttempts = checkpoint.PublishAttempts + 1,
                        LastPublishError = publishError,
                    };
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public ValueTask EraseAsync(AuditSegmentFilter filter, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            List<string> erased = [];

            foreach (var head in _heads.Values)
            {
                if (MatchesFilter(head, filter))
                {
                    erased.Add(head.Info.Key);
                }
            }

            foreach (var segment in erased)
            {
                _heads.Remove(segment);
                _linked.Remove(segment);
                _unlinked.Remove(segment);
            }

            List<Guid> checkpoints = [];

            foreach (var pair in _checkpoints)
            {
                if (erased.Contains(pair.Value.Segment))
                {
                    checkpoints.Add(pair.Key);
                }
            }

            foreach (var checkpointId in checkpoints)
            {
                _checkpoints.Remove(checkpointId);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public ValueTask ExpireAsync(string segment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_lock)
        {
            if (!_heads.TryGetValue(segment, out var head) || head.LastSequence == 0)
            {
                throw new InvalidOperationException($"The audit segment '{segment}' holds no records, so it cannot expire.");
            }

            if (head.Expired)
            {
                throw new InvalidOperationException($"The audit segment '{segment}' has expired already, so it cannot expire again.");
            }

            _linked.Remove(segment);
            _unlinked.Remove(segment);
            _heads[segment] = head with { Expired = true, Version = head.Version + 1 };

            return ValueTask.CompletedTask;
        }
    }

    /// <summary>
    ///     Reads the linked documents of a segment in ascending sequence order.
    /// </summary>
    /// <param name="segment">The segment to read.</param>
    /// <returns>The linked documents in ascending sequence order.</returns>
    public IReadOnlyList<LinkTestDocument> Linked(string segment)
    {
        lock (_lock)
        {
            return _linked.TryGetValue(segment, out var linked) ? [.. linked] : [];
        }
    }

    /// <summary>
    ///     Counts the staged documents of a segment.
    /// </summary>
    /// <param name="segment">The segment to count.</param>
    /// <returns>How many documents are still staged.</returns>
    public int UnlinkedCount(string segment)
    {
        lock (_lock)
        {
            return _unlinked.TryGetValue(segment, out var staged) ? staged.Count : 0;
        }
    }

    /// <summary>
    ///     Marks a segment expired so it closes to new writes.
    /// </summary>
    /// <param name="segment">The segment to expire.</param>
    public void MarkExpired(string segment)
    {
        lock (_lock)
        {
            if (_heads.TryGetValue(segment, out var head))
            {
                _heads[segment] = head with { Expired = true };
            }
        }
    }

    /// <summary>
    ///     Decides whether a head matches a segment filter.
    /// </summary>
    /// <param name="head">The head to test.</param>
    /// <param name="filter">The filter to match.</param>
    /// <returns><see langword="true" /> when the head matches; otherwise, <see langword="false" />.</returns>
    private static bool MatchesFilter(AuditSegmentHead head, AuditSegmentFilter filter)
    {
        if (filter.TenantId is not null && !string.Equals(head.Info.TenantId, filter.TenantId, StringComparison.Ordinal))
        {
            return false;
        }

        if (filter.Attributes is not null && filter.Attributes.Count > 0)
        {
            foreach (var pair in filter.Attributes)
            {
                if (!head.Info.Attributes.TryGetValue(pair.Key, out var value)
                    || !string.Equals(value, pair.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        if (filter.OpenedBefore.HasValue && head.Info.OpenedAt >= filter.OpenedBefore.Value)
        {
            return false;
        }

        if (!filter.IncludeExpired && head.Expired)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     A staged document with its commit stamp.
    /// </summary>
    /// <param name="Document">The staged document.</param>
    /// <param name="StagedAt">When the document was staged.</param>
    /// <param name="CommitOrder">The commit order the document staged in.</param>
    private sealed record StagedRecord(LinkTestDocument Document, DateTimeOffset StagedAt, long CommitOrder);
}
