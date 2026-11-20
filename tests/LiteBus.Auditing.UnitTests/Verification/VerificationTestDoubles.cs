using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing;
using LiteBus.Auditing.Engine;
using LiteBus.Auditing.Recording;
using LiteBus.Auditing.Hashing;

namespace LiteBus.Auditing.UnitTests.Verification;

/// <summary>
///     A stored audit document with one business field, standing in for an application-owned document type.
/// </summary>
internal sealed class VerificationTestDocument : IAuditChainDocument
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
///     The canonical form of <see cref="VerificationTestDocument" />: every field except the hash itself.
/// </summary>
internal sealed class VerificationTestCanonicalizer : IAuditCanonicalizer<VerificationTestDocument>
{
    /// <inheritdoc />
    public byte[] Canonicalize(VerificationTestDocument document)
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
///     Signs checkpoints with a fixed test key, so signature verification runs without infrastructure.
/// </summary>
internal class VerificationTestSigner : ICheckpointSigner
{
    /// <summary>
    ///     The scheme carried on every signature this signer produces.
    /// </summary>
    public const string Scheme = "test-hmac";

    /// <summary>
    ///     Gets the key the test signatures are computed with.
    /// </summary>
    private readonly byte[] _key;

    /// <summary>
    ///     Initializes a new instance of the <see cref="VerificationTestSigner" /> class.
    /// </summary>
    /// <param name="key">The key the test signatures are computed with.</param>
    public VerificationTestSigner(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);

        _key = key;
    }

    /// <inheritdoc />
    public ValueTask<AuditCheckpointSignature> SignAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(new AuditCheckpointSignature(Scheme, Compute(checkpoint)));
    }

    /// <inheritdoc />
    public ValueTask<bool> VerifyAsync(AuditCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(
            checkpoint.Signature is not null
            && string.Equals(checkpoint.Signature.Scheme, Scheme, StringComparison.Ordinal)
            && string.Equals(checkpoint.Signature.Value, Compute(checkpoint), StringComparison.Ordinal));
    }

    /// <summary>
    ///     Computes the signature value over the checkpoint identity and position.
    /// </summary>
    /// <param name="checkpoint">The checkpoint to sign.</param>
    /// <returns>The lowercase hexadecimal HMAC over the checkpoint fields.</returns>
    private string Compute(AuditCheckpoint checkpoint)
    {
        var bytes = Encoding.UTF8.GetBytes(string.Concat(
            checkpoint.Id.ToString("N"),
            "\n",
            checkpoint.Segment,
            "\n",
            checkpoint.Sequence.ToString(CultureInfo.InvariantCulture),
            "\n",
            checkpoint.Count.ToString(CultureInfo.InvariantCulture),
            "\n",
            checkpoint.Hash,
            "\n",
            checkpoint.HashScheme));

        return Convert.ToHexStringLower(HMACSHA256.HashData(_key, bytes));
    }
}

/// <summary>
///     Resolves hash schemes from a fixed lookup, throwing for unknown names like the shipped registry.
/// </summary>
internal sealed class VerificationTestSchemeRegistry : IAuditHashSchemeRegistry
{
    /// <summary>
    ///     The schemes by the name documents store.
    /// </summary>
    private readonly Dictionary<string, AuditHashScheme<VerificationTestDocument>> _schemes;

    /// <summary>
    ///     Initializes a new instance of the <see cref="VerificationTestSchemeRegistry" /> class.
    /// </summary>
    /// <param name="schemes">The schemes the registry resolves.</param>
    public VerificationTestSchemeRegistry(IEnumerable<AuditHashScheme<VerificationTestDocument>> schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);

        _schemes = new Dictionary<string, AuditHashScheme<VerificationTestDocument>>(StringComparer.Ordinal);

        foreach (var scheme in schemes)
        {
            _schemes[scheme.Name] = scheme;
        }
    }

    /// <inheritdoc />
    public AuditHashScheme<TDocument> Resolve<TDocument>(string schemeName)
        where TDocument : class, IAuditChainDocument
    {
        if (!_schemes.TryGetValue(schemeName, out var scheme))
        {
            throw new InvalidOperationException($"The hash scheme '{schemeName}' is not registered.");
        }

        return (AuditHashScheme<TDocument>) (object) scheme;
    }
}

/// <summary>
///     Builds verification test schemes, chains, checkpoints, and chain options.
/// </summary>
internal static class VerificationTestData
{
    /// <summary>
    ///     Builds a hash scheme over the test canonical form and the default link rule.
    /// </summary>
    /// <param name="name">The scheme name stored on documents.</param>
    /// <param name="hasher">The hasher, or <see langword="null" /> for plain SHA-256.</param>
    /// <returns>The scheme.</returns>
    public static AuditHashScheme<VerificationTestDocument> TestScheme(string name, IAuditHasher? hasher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new AuditHashScheme<VerificationTestDocument>(
            name,
            new VerificationTestCanonicalizer(),
            new PreviousHashLinkRule(),
            hasher ?? new Sha256AuditHasher());
    }

    /// <summary>
    ///     Appends chained documents to a segment, hashing each exactly as a chain writer would.
    /// </summary>
    /// <param name="segment">The segment to append to.</param>
    /// <param name="schemes">The scheme per document, in sequence order. Each record stores the scheme it was written with.</param>
    /// <param name="cancellationToken">The token used to cancel the build.</param>
    /// <returns>The chained documents in sequence order.</returns>
    public static async Task<List<VerificationTestDocument>> BuildChainAsync(
        string segment,
        IReadOnlyList<AuditHashScheme<VerificationTestDocument>> schemes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(schemes);

        var documents = new List<VerificationTestDocument>(schemes.Count);
        string? previousHash = null;

        for (var index = 0; index < schemes.Count; index++)
        {
            var scheme = schemes[index];
            var document = new VerificationTestDocument
            {
                Segment = segment,
                Sequence = index + 1,
                PreviousHash = previousHash,
                HashScheme = scheme.Name,
                Action = string.Concat("orders.step-", (index + 1).ToString(CultureInfo.InvariantCulture)),
            };

            var canonical = scheme.Canonicalizer.Canonicalize(document);
            var linked = scheme.LinkRule.Link(
                new AuditLinkInput(segment, document.Sequence, document.PreviousHash, canonical));
            document.Hash = await scheme.Hasher.HashAsync(linked, cancellationToken).ConfigureAwait(false);

            documents.Add(document);
            previousHash = document.Hash;
        }

        return documents;
    }

    /// <summary>
    ///     Appends chained documents all written with one scheme.
    /// </summary>
    /// <param name="segment">The segment to append to.</param>
    /// <param name="scheme">The scheme every document is written with.</param>
    /// <param name="count">How many documents to append.</param>
    /// <param name="cancellationToken">The token used to cancel the build.</param>
    /// <returns>The chained documents in sequence order.</returns>
    public static Task<List<VerificationTestDocument>> BuildChainAsync(
        string segment,
        AuditHashScheme<VerificationTestDocument> scheme,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var schemes = new AuditHashScheme<VerificationTestDocument>[count];

        for (var index = 0; index < count; index++)
        {
            schemes[index] = scheme;
        }

        return BuildChainAsync(segment, schemes, cancellationToken);
    }

    /// <summary>
    ///     Recomputes the hash of one document after its fields changed, the way a writer hashes at append time.
    /// </summary>
    /// <param name="document">The document to rehash.</param>
    /// <param name="scheme">The scheme the document names.</param>
    /// <param name="cancellationToken">The token used to cancel the hash.</param>
    /// <returns>A task representing the asynchronous rehash.</returns>
    public static async Task RehashAsync(
        VerificationTestDocument document,
        AuditHashScheme<VerificationTestDocument> scheme,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentException.ThrowIfNullOrWhiteSpace(document.Segment);

        var canonical = scheme.Canonicalizer.Canonicalize(document);
        var linked = scheme.LinkRule.Link(
            new AuditLinkInput(document.Segment, document.Sequence, document.PreviousHash, canonical));
        document.Hash = await scheme.Hasher.HashAsync(linked, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Cuts the checkpoint the head of a chain implies.
    /// </summary>
    /// <param name="segment">The segment the checkpoint covers.</param>
    /// <param name="documents">The chain in sequence order. Must hold at least one document.</param>
    /// <param name="createdAt">When the checkpoint was cut, or <see langword="null" /> for the current instant.</param>
    /// <returns>The checkpoint covering the head.</returns>
    public static AuditCheckpoint CheckpointFor(
        string segment,
        IReadOnlyList<VerificationTestDocument> documents,
        DateTimeOffset? createdAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(documents);

        if (documents.Count == 0)
        {
            throw new ArgumentException("The chain must hold at least one document.", nameof(documents));
        }

        var head = documents[documents.Count - 1];

        return new AuditCheckpoint(
            Guid.NewGuid(),
            segment,
            head.Sequence,
            documents.Count,
            head.Hash!,
            head.HashScheme!,
            AuditCheckpointKind.Regular,
            createdAt ?? DateTimeOffset.UtcNow);
    }

    /// <summary>
    ///     Builds chain options with the given source, signer, and threshold configuration.
    /// </summary>
    /// <param name="sourceTypes">The checkpoint source types verification compares against.</param>
    /// <param name="signerTypes">The checkpoint signer types verification verifies with.</param>
    /// <param name="unlinkedAfter">The age past which a staged record is reported, or <see langword="null" /> for the default.</param>
    /// <param name="checkpointStaleAfter">The age past which a last checkpoint is stale, or <see langword="null" /> for none.</param>
    /// <param name="checkpointPolicy">The checkpoint policy the chain cuts by, or <see langword="null" /> for the default.</param>
    /// <returns>The chain options.</returns>
    public static AuditHashChainOptions ChainOptions(
        IReadOnlyList<Type>? sourceTypes = null,
        IReadOnlyList<Type>? signerTypes = null,
        TimeSpan? unlinkedAfter = null,
        TimeSpan? checkpointStaleAfter = null,
        ICheckpointPolicy? checkpointPolicy = null)
    {
        return new AuditHashChainOptions
        {
            SourceTypes = sourceTypes ?? [],
            SignerTypes = signerTypes ?? [],
            UnlinkedAfter = unlinkedAfter ?? AuditHashChainOptions.DefaultUnlinkedAfter,
            CheckpointStaleAfter = checkpointStaleAfter,
            CheckpointPolicy = checkpointPolicy,
        };
    }
}

/// <summary>
///     A clock with a settable instant for age-gated verification tests.
/// </summary>
internal sealed class VerificationTestTimeProvider : TimeProvider
{
    /// <summary>
    ///     The instant the clock reports.
    /// </summary>
    private DateTimeOffset _utcNow;

    /// <summary>
    ///     Initializes a new instance of the <see cref="VerificationTestTimeProvider" /> class.
    /// </summary>
    /// <param name="utcNow">The instant the clock reports.</param>
    public VerificationTestTimeProvider(DateTimeOffset utcNow)
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
internal sealed class VerificationTestStore : IAuditChainStore<VerificationTestDocument>
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
    private readonly Dictionary<string, List<VerificationTestDocument>> _linked = new(StringComparer.Ordinal);

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
    ///     Initializes a new instance of the <see cref="VerificationTestStore" /> class.
    /// </summary>
    /// <param name="timeProvider">The clock that stamps staged records, or <see langword="null" /> for the system clock.</param>
    public VerificationTestStore(TimeProvider? timeProvider = null)
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
        IReadOnlyList<VerificationTestDocument> documents,
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
    public ValueTask StageAsync(string segment, VerificationTestDocument document, CancellationToken cancellationToken)
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

            if (!_heads.ContainsKey(segment))
            {
                _heads[segment] = new AuditSegmentHead(
                    new AuditSegmentInfo(
                        segment,
                        null,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        _timeProvider.GetUtcNow()),
                    0,
                    null,
                    0,
                    null,
                    null,
                    false,
                    0);
            }

            return ValueTask.CompletedTask;
        }
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<VerificationTestDocument> ListUnlinkedAsync(
        string segment,
        TimeSpan? olderThan,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        List<StagedRecord> snapshot;

        lock (_lock)
        {
            if (!_unlinked.TryGetValue(segment, out var staged))
            {
                yield break;
            }

            snapshot = [.. staged];
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
    public async IAsyncEnumerable<VerificationTestDocument> ReadSegmentAsync(
        string segment,
        long fromSequence,
        long? toSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        List<VerificationTestDocument> snapshot;

        lock (_lock)
        {
            if (!_linked.TryGetValue(segment, out var linked))
            {
                yield break;
            }

            snapshot = [.. linked];
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
    ///     Seeds linked documents and their head directly, bypassing version checks for test setup.
    /// </summary>
    /// <param name="segment">The segment to seed.</param>
    /// <param name="documents">The linked documents in ascending sequence order.</param>
    public void SeedLinked(string segment, IReadOnlyList<VerificationTestDocument> documents)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(documents);

        lock (_lock)
        {
            List<VerificationTestDocument> linked = [.. documents];
            linked.Sort(static (left, right) => left.Sequence.CompareTo(right.Sequence));
            _linked[segment] = linked;

            var head = linked.Count == 0
                ? new AuditSegmentHead(
                    new AuditSegmentInfo(
                        segment,
                        null,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        _timeProvider.GetUtcNow()),
                    0,
                    null,
                    0,
                    null,
                    null,
                    false,
                    1)
                : new AuditSegmentHead(
                    new AuditSegmentInfo(
                        segment,
                        null,
                        new Dictionary<string, string>(StringComparer.Ordinal),
                        _timeProvider.GetUtcNow()),
                    linked[linked.Count - 1].Sequence,
                    linked[linked.Count - 1].Hash,
                    linked.Count,
                    null,
                    null,
                    false,
                    1);

            _heads[segment] = head;
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
    private sealed record StagedRecord(VerificationTestDocument Document, DateTimeOffset StagedAt, long CommitOrder);
}

/// <summary>
///     A checkpoint source holding one fixed checkpoint per segment for verification tests.
/// </summary>
internal class VerificationTestSource : ICheckpointSource
{
    /// <summary>
    ///     The checkpoints by segment.
    /// </summary>
    private readonly Dictionary<string, AuditCheckpoint> _checkpoints = new(StringComparer.Ordinal);

    /// <summary>
    ///     Sets the checkpoint the source holds for a segment.
    /// </summary>
    /// <param name="checkpoint">The checkpoint the source holds.</param>
    public void SetCheckpoint(AuditCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);

        _checkpoints[checkpoint.Segment] = checkpoint;
    }

    /// <inheritdoc />
    public ValueTask<AuditCheckpoint?> GetLatestAsync(string segment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _checkpoints.TryGetValue(segment, out var checkpoint);

        return ValueTask.FromResult(checkpoint);
    }
}

/// <summary>
///     A checkpoint source that always throws, standing in for an unreachable anchor.
/// </summary>
internal sealed class VerificationUnreachableSource : ICheckpointSource
{
    /// <inheritdoc />
    public ValueTask<AuditCheckpoint?> GetLatestAsync(string segment, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        throw new InvalidOperationException("The anchor could not be reached.");
    }
}

/// <summary>
///     A service provider resolving verification pieces from a fixed map for tests.
/// </summary>
internal sealed class VerificationTestServiceProvider : IServiceProvider
{
    /// <summary>
    ///     The services by implementation type.
    /// </summary>
    private readonly Dictionary<Type, object> _services = [];

    /// <summary>
    ///     Registers a service instance under its implementation type.
    /// </summary>
    /// <param name="service">The service instance.</param>
    /// <typeparam name="TService">The implementation type the instance is registered under.</typeparam>
    public void Add<TService>(TService service)
        where TService : class
    {
        ArgumentNullException.ThrowIfNull(service);

        _services[typeof(TService)] = service;
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType)
    {
        _services.TryGetValue(serviceType, out var service);

        return service;
    }
}
