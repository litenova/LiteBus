using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Engine;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Runtime.Abstractions.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LiteBus.Auditing.Verification;

/// <summary>
///     Verifies one audit chain segment against every configured checkpoint source, streaming records by pages.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         The engine behind <see cref="IAuditChain" /> verification: the segment is read by pages through
///         <see cref="IAuditChainStore{TDocument}.ReadSegmentAsync" /> in ascending sequence order, so a large segment
///         verifies with bounded memory. Each document's predecessor link is checked and its hash is recomputed with
///         the scheme the document names, resolved through <see cref="IAuditHashSchemeRegistry" />.
///     </para>
///     <para>
///         Every source the chain names in <see cref="AuditHashChainOptions.SourceTypes" /> is resolved from the
///         container and read for its latest checkpoint, with the store's own source first. Each source produces one
///         <see cref="AuditCheckpointComparison" />: a source that disagrees produces hash, count, and signature
///         breaks beside its comparison, while a source that cannot be reached produces a comparison with
///         <see cref="AuditCheckpointComparison.SourceUnreachable" /> and no break, so an operational gap stays
///         separate from an integrity break.
///     </para>
///     <para>
///         A verification request may trust a sequence: only the records after it are checked and no gap is reported
///         for the trusted prefix. The trusted position must hold a document; otherwise the verification reports
///         <see cref="AuditChainBreakKind.CheckpointMissing" /> and checks nothing else.
///     </para>
/// </remarks>
internal sealed class AuditVerifier<TDocument> : IAuditVerifier
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     The durable chain operations segments, unlinked records, and checkpoints are read from. Held for the life
    ///     of the verifier.
    /// </summary>
    private readonly IAuditChainStore<TDocument> _store;

    /// <summary>
    ///     The registry that resolves the hash scheme each document names. Held for the life of the verifier.
    /// </summary>
    private readonly IAuditHashSchemeRegistry _schemes;

    /// <summary>
    ///     The chain configuration naming the checkpoint sources and signers and carrying the break thresholds. Held
    ///     for the life of the verifier.
    /// </summary>
    private readonly AuditHashChainOptions _chainOptions;

    /// <summary>
    ///     Resolves pieces the builder named as types, or <see langword="null" /> when every piece was given as an
    ///     instance. Held for the life of the verifier.
    /// </summary>
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    ///     The clock that ages checkpoints for the stale break. Held for the life of the verifier.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     The logger unreachable checkpoint sources are reported to. Held for the life of the verifier.
    /// </summary>
    private readonly ILogger<AuditVerifier<TDocument>> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditVerifier{TDocument}" /> class.
    /// </summary>
    /// <param name="store">The durable chain operations verification reads from.</param>
    /// <param name="schemes">The registry that resolves the hash scheme each document names.</param>
    /// <param name="chainOptions">The chain configuration naming the checkpoint sources and signers.</param>
    /// <param name="serviceProvider">
    ///     Resolves pieces the builder named as types, or <see langword="null" /> when every piece was given as an
    ///     instance.
    /// </param>
    /// <param name="timeProvider">The clock that ages checkpoints, or <see langword="null" /> for the system clock.</param>
    /// <param name="logger">The logger unreachable sources are reported to, or <see langword="null" /> for none.</param>
    public AuditVerifier(
        IAuditChainStore<TDocument> store,
        IAuditHashSchemeRegistry schemes,
        AuditHashChainOptions chainOptions,
        IServiceProvider? serviceProvider = null,
        TimeProvider? timeProvider = null,
        ILogger<AuditVerifier<TDocument>>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(schemes);
        ArgumentNullException.ThrowIfNull(chainOptions);

        _store = store;
        _schemes = schemes;
        _chainOptions = chainOptions;
        _serviceProvider = serviceProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<AuditVerifier<TDocument>>.Instance;
    }

    /// <inheritdoc />
    public async ValueTask<AuditChainVerification> VerifyAsync(
        AuditChainVerificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Segment);

        if (request.FromSequence.HasValue)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(request.FromSequence.Value, 1);
        }

        var segment = request.Segment;
        var fromSequence = request.FromSequence;
        var unlinkedAfter = request.UnlinkedOlderThan ?? _chainOptions.UnlinkedAfter;
        var staleAfter = request.CheckpointStaleAfter ?? _chainOptions.CheckpointStaleAfter;

        var loaded = await LoadSourceCheckpointsAsync(segment, cancellationToken).ConfigureAwait(false);
        var signers = ResolveSigners();

        string? trustedHash = null;

        if (fromSequence.HasValue)
        {
            trustedHash = await ReadTrustedHashAsync(segment, fromSequence.Value, cancellationToken)
                .ConfigureAwait(false);

            if (trustedHash is null)
            {
                return BuildMissingTrustedVerification(segment, fromSequence.Value, loaded);
            }
        }

        var chain = await VerifyChainAsync(segment, fromSequence, trustedHash, loaded, signers, cancellationToken)
            .ConfigureAwait(false);

        await ReportUnlinkedAsync(segment, unlinkedAfter, chain.Breaks, cancellationToken).ConfigureAwait(false);
        await ReportUnpublishedAsync(segment, chain.Breaks, cancellationToken).ConfigureAwait(false);
        ReportStale(segment, staleAfter, chain, cancellationToken);
        await ReportMissingAsync(segment, fromSequence, loaded, chain, cancellationToken).ConfigureAwait(false);

        chain.Breaks.Sort(static (left, right) =>
            left.Sequence != right.Sequence
                ? left.Sequence.CompareTo(right.Sequence)
                : left.Kind.CompareTo(right.Kind));

        return new AuditChainVerification
        {
            Segment = segment,
            RecordCount = chain.RecordCount,
            HeadSequence = chain.HeadSequence,
            HeadHash = chain.HeadHash,
            Breaks = [.. chain.Breaks],
            LatestCheckpoint = chain.StoredCheckpoint,
            MatchesLatestCheckpoint = chain.MatchesStoredCheckpoint,
            CheckpointSignatureValid = chain.StoredSignatureValid,
            Comparisons = [.. chain.Comparisons],
        };
    }

    /// <summary>
    ///     Reads the hash at the trusted sequence, or <see langword="null" /> when the trusted position holds no
    ///     document.
    /// </summary>
    /// <param name="segment">The segment under verification.</param>
    /// <param name="fromSequence">The trusted sequence.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The trusted hash, or <see langword="null" /> when the trusted position holds no document.</returns>
    private async ValueTask<string?> ReadTrustedHashAsync(
        string segment,
        long fromSequence,
        CancellationToken cancellationToken)
    {
        await foreach (var document in _store
            .ReadSegmentAsync(segment, fromSequence, fromSequence, cancellationToken)
            .ConfigureAwait(false))
        {
            return document.Hash;
        }

        return null;
    }

    /// <summary>
    ///     Builds the verification for a trusted sequence that holds no document: one
    ///     <see cref="AuditChainBreakKind.CheckpointMissing" /> break and no chain pass.
    /// </summary>
    /// <param name="segment">The segment under verification.</param>
    /// <param name="fromSequence">The trusted sequence that holds no document.</param>
    /// <param name="loaded">The checkpoint each source holds.</param>
    /// <returns>The verification reporting the missing trusted checkpoint.</returns>
    /// <remarks>
    ///     The suffix cannot be verified without the trusted hash, so no chain, unlinked, unpublished, stale, or
    ///     source comparison runs: the missing trusted position is the one finding.
    /// </remarks>
    private static AuditChainVerification BuildMissingTrustedVerification(
        string segment,
        long fromSequence,
        IReadOnlyList<LoadedCheckpoint> loaded)
    {
        List<AuditCheckpointComparison> comparisons = new(loaded.Count);
        AuditCheckpoint? storedCheckpoint = null;

        for (var index = 0; index < loaded.Count; index++)
        {
            var entry = loaded[index];

            if (entry.Unreachable)
            {
                comparisons.Add(new AuditCheckpointComparison(entry.SourceName, null, null, null, null)
                {
                    SourceUnreachable = true,
                });

                continue;
            }

            comparisons.Add(new AuditCheckpointComparison(entry.SourceName, entry.Checkpoint, null, null, null));

            if (index == 0)
            {
                storedCheckpoint = entry.Checkpoint;
            }
        }

        List<AuditChainBreak> breaks =
        [
            new AuditChainBreak(
                fromSequence,
                AuditChainBreakKind.CheckpointMissing,
                $"The checkpoint at sequence {fromSequence} of segment '{segment}' does not exist, so the records after it cannot be verified."),
        ];

        return new AuditChainVerification
        {
            Segment = segment,
            RecordCount = 0,
            HeadSequence = null,
            HeadHash = null,
            Breaks = breaks,
            LatestCheckpoint = storedCheckpoint,
            MatchesLatestCheckpoint = null,
            CheckpointSignatureValid = null,
            Comparisons = comparisons,
        };
    }

    /// <summary>
    ///     Streams the segment once, checking links and hashes and comparing every reachable checkpoint.
    /// </summary>
    /// <param name="segment">The segment under verification.</param>
    /// <param name="fromSequence">The trusted sequence, or <see langword="null" /> to verify the whole segment.</param>
    /// <param name="trustedHash">The hash at the trusted sequence, or <see langword="null" /> for a whole pass.</param>
    /// <param name="loaded">The checkpoint each source holds.</param>
    /// <param name="signers">The signers checkpoint signatures are verified with.</param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    /// <returns>The chain pass: breaks, head, comparisons, and the stored checkpoint comparison.</returns>
    private async ValueTask<ChainPass> VerifyChainAsync(
        string segment,
        long? fromSequence,
        string? trustedHash,
        IReadOnlyList<LoadedCheckpoint> loaded,
        IReadOnlyList<ICheckpointSigner> signers,
        CancellationToken cancellationToken)
    {
        HashSet<long> compareSequences = [];

        foreach (var entry in loaded)
        {
            if (!entry.Unreachable
                && entry.Checkpoint is not null
                && (!fromSequence.HasValue || entry.Checkpoint.Sequence > fromSequence.Value))
            {
                compareSequences.Add(entry.Checkpoint.Sequence);
            }
        }

        Dictionary<long, string?> hashAtSequence = [];
        Dictionary<long, long> countUpTo = [];

        foreach (var sequence in compareSequences)
        {
            countUpTo[sequence] = 0;
        }

        List<AuditChainBreak> breaks = [];
        Dictionary<string, AuditHashScheme<TDocument>?> schemeCache = new(StringComparer.Ordinal);

        var recordCount = 0L;
        long? headSequence = fromSequence;
        string? headHash = trustedHash;
        string? previousHash = trustedHash;
        var highestSoFar = fromSequence ?? 0;
        var readFrom = fromSequence.HasValue ? fromSequence.Value + 1 : 1;

        await foreach (var document in _store
            .ReadSegmentAsync(segment, readFrom, null, cancellationToken)
            .ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (fromSequence.HasValue)
            {
                ReportStreamingStructuralBreakFrom(segment, document, fromSequence.Value, ref highestSoFar, breaks);
            }
            else
            {
                ReportStreamingStructuralBreak(segment, document, ref highestSoFar, breaks);
            }

            if (!string.Equals(document.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                breaks.Add(new AuditChainBreak(
                    document.Sequence,
                    AuditChainBreakKind.PreviousHashMismatch,
                    $"Sequence {document.Sequence} of segment '{segment}' carries a previous hash that does not match the hash stored before it."));
            }

            await CheckDocumentHashAsync(segment, document, schemeCache, breaks, cancellationToken)
                .ConfigureAwait(false);

            foreach (var sequence in compareSequences)
            {
                if (document.Sequence <= sequence)
                {
                    countUpTo[sequence]++;
                }
            }

            if (compareSequences.Contains(document.Sequence) && !hashAtSequence.ContainsKey(document.Sequence))
            {
                hashAtSequence[document.Sequence] = document.Hash;
            }

            previousHash = document.Hash;
            recordCount++;
            headSequence = document.Sequence;
            headHash = document.Hash;
        }

        List<AuditCheckpointComparison> comparisons = new(loaded.Count);
        AuditCheckpoint? storedCheckpoint = null;
        bool? storedHashMatches = null;
        bool? storedCountMatches = null;
        bool? storedSignatureValid = null;

        for (var index = 0; index < loaded.Count; index++)
        {
            var entry = loaded[index];

            if (entry.Unreachable)
            {
                comparisons.Add(new AuditCheckpointComparison(entry.SourceName, null, null, null, null)
                {
                    SourceUnreachable = true,
                });

                continue;
            }

            if (entry.Checkpoint is null)
            {
                comparisons.Add(new AuditCheckpointComparison(entry.SourceName, null, null, null, null));

                continue;
            }

            var checkpoint = entry.Checkpoint;

            if (fromSequence.HasValue && checkpoint.Sequence <= fromSequence.Value)
            {
                var trustedSignatureValid = await VerifyCheckpointSignaturesAsync(checkpoint, signers, breaks, cancellationToken)
                    .ConfigureAwait(false);

                comparisons.Add(new AuditCheckpointComparison(
                    entry.SourceName, checkpoint, null, null, trustedSignatureValid));

                if (index == 0)
                {
                    storedCheckpoint = checkpoint;
                    storedSignatureValid = trustedSignatureValid;
                }

                continue;
            }

            bool hashMatches;

            if (!hashAtSequence.TryGetValue(checkpoint.Sequence, out var coveredHash))
            {
                hashMatches = false;
                breaks.Add(new AuditChainBreak(
                    checkpoint.Sequence,
                    AuditChainBreakKind.CheckpointHashMismatch,
                    $"The checkpoint at sequence {checkpoint.Sequence} of segment '{segment}' from source '{entry.SourceName}' names a hash, but the segment holds no document at that sequence."));
            }
            else if (!string.Equals(coveredHash, checkpoint.Hash, StringComparison.Ordinal))
            {
                hashMatches = false;
                breaks.Add(new AuditChainBreak(
                    checkpoint.Sequence,
                    AuditChainBreakKind.CheckpointHashMismatch,
                    $"The hash at sequence {checkpoint.Sequence} of segment '{segment}' does not match the checkpoint hash from source '{entry.SourceName}'. The segment was rewritten after the checkpoint was cut."));
            }
            else
            {
                hashMatches = true;
            }

            bool? countMatches = null;

            if (!fromSequence.HasValue)
            {
                var covered = countUpTo.TryGetValue(checkpoint.Sequence, out var counted) ? counted : 0;

                if (covered < checkpoint.Count)
                {
                    countMatches = false;
                    breaks.Add(new AuditChainBreak(
                        checkpoint.Sequence,
                        AuditChainBreakKind.CheckpointCountMismatch,
                        $"Segment '{segment}' holds {covered} documents up to sequence {checkpoint.Sequence}, but the checkpoint from source '{entry.SourceName}' counted {checkpoint.Count}. A document the checkpoint covered is missing."));
                }
                else
                {
                    countMatches = true;
                }
            }

            var signatureValid = await VerifyCheckpointSignaturesAsync(checkpoint, signers, breaks, cancellationToken)
                .ConfigureAwait(false);

            comparisons.Add(new AuditCheckpointComparison(
                entry.SourceName, checkpoint, hashMatches, countMatches, signatureValid));

            if (index == 0)
            {
                storedCheckpoint = checkpoint;
                storedHashMatches = hashMatches;
                storedCountMatches = countMatches;
                storedSignatureValid = signatureValid;
            }
        }

        bool? matchesStoredCheckpoint = null;

        if (storedCheckpoint is not null)
        {
            if (storedHashMatches == false || storedCountMatches == false)
            {
                matchesStoredCheckpoint = false;
            }
            else if (storedHashMatches == true && (storedCountMatches == true || fromSequence.HasValue))
            {
                matchesStoredCheckpoint = true;
            }
        }

        return new ChainPass(
            breaks,
            recordCount,
            headSequence,
            headHash,
            comparisons,
            storedCheckpoint,
            matchesStoredCheckpoint,
            storedSignatureValid);
    }

    /// <summary>
    ///     Recomputes the hash of one document with the scheme it names.
    /// </summary>
    /// <param name="segment">The segment under verification, named in each break.</param>
    /// <param name="document">The document to check.</param>
    /// <param name="schemeCache">The schemes resolved so far, with <see langword="null" /> for unknown names.</param>
    /// <param name="breaks">The breaks collected so far.</param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    /// <returns>A task representing the asynchronous check.</returns>
    /// <remarks>
    ///     A document naming no scheme, or one the registry does not hold, is reported without a recomputation. The
    ///     registry reports an unknown name with <see cref="InvalidOperationException" /> or
    ///     <see cref="KeyNotFoundException" />; any other failure is a configuration fault and propagates.
    /// </remarks>
    private async ValueTask CheckDocumentHashAsync(
        string segment,
        TDocument document,
        Dictionary<string, AuditHashScheme<TDocument>?> schemeCache,
        List<AuditChainBreak> breaks,
        CancellationToken cancellationToken)
    {
        if (document.HashScheme is null)
        {
            breaks.Add(new AuditChainBreak(
                document.Sequence,
                AuditChainBreakKind.UnknownHashScheme,
                $"Sequence {document.Sequence} of segment '{segment}' names no hash scheme."));

            return;
        }

        if (!schemeCache.TryGetValue(document.HashScheme, out var scheme))
        {
            try
            {
                scheme = _schemes.Resolve<TDocument>(document.HashScheme);
            }
            catch (InvalidOperationException)
            {
                scheme = null;
            }
            catch (KeyNotFoundException)
            {
                scheme = null;
            }

            schemeCache[document.HashScheme] = scheme;
        }

        if (scheme is null)
        {
            breaks.Add(new AuditChainBreak(
                document.Sequence,
                AuditChainBreakKind.UnknownHashScheme,
                $"Sequence {document.Sequence} of segment '{segment}' names the hash scheme '{document.HashScheme}', which is not registered."));

            return;
        }

        var canonical = scheme.Canonicalizer.Canonicalize(document);
        var linked = scheme.LinkRule.Link(
            new AuditLinkInput(segment, document.Sequence, document.PreviousHash, canonical));
        var recomputed = await scheme.Hasher.HashAsync(linked, cancellationToken).ConfigureAwait(false);

        if (!string.Equals(recomputed, document.Hash, StringComparison.Ordinal))
        {
            breaks.Add(new AuditChainBreak(
                document.Sequence,
                AuditChainBreakKind.HashMismatch,
                $"Sequence {document.Sequence} of segment '{segment}' carries a hash that does not match its recomputed hash. The document was altered after it was written."));
        }
    }

    /// <summary>
    ///     Verifies the signatures a checkpoint carries with every configured signer.
    /// </summary>
    /// <param name="checkpoint">The checkpoint with its signatures.</param>
    /// <param name="signers">The signers every checkpoint is verified with.</param>
    /// <param name="breaks">The breaks collected so far.</param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    /// <returns>
    ///     <see langword="null" /> when no signer is configured; otherwise, <see langword="true" /> when every signer
    ///     verifies the checkpoint.
    /// </returns>
    /// <remarks>
    ///     Every configured signer must verify the checkpoint, because the lifecycle signs every cut checkpoint with
    ///     every signer. A signer that throws fails the verification loudly instead of reporting an invalid
    ///     signature: a key or timestamping failure is an operational fault, not evidence of tampering.
    /// </remarks>
    private static async ValueTask<bool?> VerifyCheckpointSignaturesAsync(
        AuditCheckpoint checkpoint,
        IReadOnlyList<ICheckpointSigner> signers,
        List<AuditChainBreak> breaks,
        CancellationToken cancellationToken)
    {
        if (signers.Count == 0)
        {
            return null;
        }

        foreach (var signer in signers)
        {
            var valid = await signer.VerifyAsync(checkpoint, cancellationToken).ConfigureAwait(false);

            if (!valid)
            {
                breaks.Add(new AuditChainBreak(
                    checkpoint.Sequence,
                    AuditChainBreakKind.CheckpointSignatureInvalid,
                    $"The signature on the checkpoint at sequence {checkpoint.Sequence} of segment '{checkpoint.Segment}' is missing or invalid."));

                return false;
            }
        }

        return true;
    }

    /// <summary>
    ///     Reports one <see cref="AuditChainBreakKind.Unlinked" /> break per record staged longer than the threshold.
    /// </summary>
    /// <param name="segment">The segment under verification, named in each break.</param>
    /// <param name="unlinkedAfter">The age past which a staged record is reported.</param>
    /// <param name="breaks">The breaks collected so far.</param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    /// <returns>A task representing the asynchronous report.</returns>
    /// <remarks>
    ///     Unlinked records carry no chain position, so every break sits at sequence zero. The store filters by age,
    ///     so every record it returns is past the threshold.
    /// </remarks>
    private async ValueTask ReportUnlinkedAsync(
        string segment,
        TimeSpan unlinkedAfter,
        List<AuditChainBreak> breaks,
        CancellationToken cancellationToken)
    {
        await foreach (var document in _store.ListUnlinkedAsync(segment, unlinkedAfter, cancellationToken).ConfigureAwait(false))
        {
            _ = document;

            breaks.Add(new AuditChainBreak(
                0,
                AuditChainBreakKind.Unlinked,
                $"A record staged in segment '{segment}' remains unlinked past {unlinkedAfter}. The linker has not assigned it a chain position."));
        }
    }

    /// <summary>
    ///     Reports one <see cref="AuditChainBreakKind.CheckpointUnpublished" /> break per stored checkpoint of the
    ///     segment that still waits for publication.
    /// </summary>
    /// <param name="segment">The segment under verification, named in each break.</param>
    /// <param name="breaks">The breaks collected so far.</param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    /// <returns>A task representing the asynchronous report.</returns>
    private async ValueTask ReportUnpublishedAsync(
        string segment,
        List<AuditChainBreak> breaks,
        CancellationToken cancellationToken)
    {
        await foreach (var checkpoint in _store.ListUnpublishedAsync(cancellationToken).ConfigureAwait(false))
        {
            if (!string.Equals(checkpoint.Segment, segment, StringComparison.Ordinal))
            {
                continue;
            }

            var detail = checkpoint.LastPublishError is null
                ? $"The checkpoint at sequence {checkpoint.Sequence} of segment '{segment}' still waits for publication after {checkpoint.PublishAttempts} attempts."
                : $"The checkpoint at sequence {checkpoint.Sequence} of segment '{segment}' still waits for publication after {checkpoint.PublishAttempts} attempts. Last error: {checkpoint.LastPublishError}";

            breaks.Add(new AuditChainBreak(
                checkpoint.Sequence,
                AuditChainBreakKind.CheckpointUnpublished,
                detail));
        }
    }

    /// <summary>
    ///     Reports a <see cref="AuditChainBreakKind.CheckpointStale" /> break when the stored checkpoint is older than
    ///     the threshold while records exist after it.
    /// </summary>
    /// <param name="segment">The segment under verification, named in the break.</param>
    /// <param name="staleAfter">The age past which a last checkpoint is stale, or <see langword="null" /> when staleness is never reported.</param>
    /// <param name="chain">The chain pass carrying the head and the stored checkpoint comparison.</param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    private void ReportStale(
        string segment,
        TimeSpan? staleAfter,
        ChainPass chain,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!staleAfter.HasValue)
        {
            return;
        }

        if (chain.StoredCheckpoint is null || !chain.HeadSequence.HasValue)
        {
            return;
        }

        if (chain.HeadSequence.Value <= chain.StoredCheckpoint.Sequence)
        {
            return;
        }

        var age = _timeProvider.GetUtcNow() - chain.StoredCheckpoint.CreatedAt;

        if (age <= staleAfter.Value)
        {
            return;
        }

        chain.Breaks.Add(new AuditChainBreak(
            chain.StoredCheckpoint.Sequence,
            AuditChainBreakKind.CheckpointStale,
            $"The last checkpoint of segment '{segment}' was cut {age} ago and records exist after it. The segment stopped checkpointing."));
    }

    /// <summary>
    ///     Reports a <see cref="AuditChainBreakKind.CheckpointMissing" /> break when the segment holds records but no
    ///     reachable source holds a checkpoint although the chain's checkpoint policy says one is due.
    /// </summary>
    /// <param name="segment">The segment under verification, named in the break.</param>
    /// <param name="fromSequence">The trusted sequence, or <see langword="null" /> for a whole pass.</param>
    /// <param name="loaded">The checkpoint each source holds.</param>
    /// <param name="chain">The chain pass carrying the head and the breaks collected so far.</param>
    /// <param name="cancellationToken">The token that cancels the policy evaluation.</param>
    /// <returns>A task that completes when the check has run.</returns>
    /// <remarks>
    ///     <para>
    ///         A trusted suffix never reports a missing checkpoint: trusting a position means a checkpoint was cut. An
    ///         unreachable stored source also reports nothing: the checkpoint may exist where the read cannot reach.
    ///     </para>
    ///     <para>
    ///         A segment younger than its first checkpoint is not a break. The default policy cuts every 100 records,
    ///         so a segment of 40 records legitimately has none. The verifier asks the same policy the checkpoint
    ///         lifecycle uses, with no previous checkpoint, and reports the break only when that policy would already
    ///         have cut one.
    ///     </para>
    /// </remarks>
    private async ValueTask ReportMissingAsync(
        string segment,
        long? fromSequence,
        IReadOnlyList<LoadedCheckpoint> loaded,
        ChainPass chain,
        CancellationToken cancellationToken)
    {
        if (fromSequence.HasValue)
        {
            return;
        }

        if (chain.RecordCount == 0)
        {
            return;
        }

        foreach (var entry in loaded)
        {
            if (!entry.Unreachable && entry.Checkpoint is not null)
            {
                return;
            }
        }

        var storedReachable = loaded.Count > 0 && !loaded[0].Unreachable;

        if (loaded.Count != 0 && !storedReachable)
        {
            return;
        }

        var candidate = new AuditCheckpointCandidate(
            segment,
            chain.HeadSequence ?? 0,
            chain.RecordCount,
            LastCheckpointSequence: null,
            LastCheckpointAt: null,
            _timeProvider.GetUtcNow());

        var due = await ResolveCheckpointPolicy().IsDueAsync(candidate, cancellationToken).ConfigureAwait(false);

        if (!due)
        {
            return;
        }

        chain.Breaks.Add(new AuditChainBreak(
            chain.HeadSequence ?? 0,
            AuditChainBreakKind.CheckpointMissing,
            $"Segment '{segment}' holds {chain.RecordCount} records but no checkpoint was ever cut."));
    }

    /// <summary>
    ///     Reports the structural break of one streamed document against the running head.
    /// </summary>
    /// <param name="segment">The segment under verification, named in each break.</param>
    /// <param name="document">The streamed document.</param>
    /// <param name="highestSoFar">The highest sequence seen so far, advanced past the document.</param>
    /// <param name="breaks">The breaks collected so far.</param>
    private static void ReportStreamingStructuralBreak(
        string segment,
        TDocument document,
        ref long highestSoFar,
        List<AuditChainBreak> breaks)
    {
        if (document.Sequence < 1)
        {
            breaks.Add(new AuditChainBreak(
                document.Sequence,
                AuditChainBreakKind.Duplicate,
                $"Sequence {document.Sequence} in segment '{segment}' is not a valid chain position. Chain sequences start at one."));

            return;
        }

        if (document.Sequence <= highestSoFar)
        {
            breaks.Add(new AuditChainBreak(
                document.Sequence,
                AuditChainBreakKind.Duplicate,
                $"Sequence {document.Sequence} appears more than once in segment '{segment}'."));

            return;
        }

        for (var missing = highestSoFar + 1; missing < document.Sequence; missing++)
        {
            breaks.Add(new AuditChainBreak(
                missing,
                AuditChainBreakKind.Gap,
                $"Sequence {missing} is missing from segment '{segment}'."));
        }

        highestSoFar = document.Sequence;
    }

    /// <summary>
    ///     Reports the structural break of one streamed suffix document against the running head.
    /// </summary>
    /// <param name="segment">The segment under verification, named in each break.</param>
    /// <param name="document">The streamed suffix document.</param>
    /// <param name="trustedSequence">The sequence the trusted checkpoint covers.</param>
    /// <param name="highestSoFar">The highest sequence seen so far, advanced past the document.</param>
    /// <param name="breaks">The breaks collected so far.</param>
    private static void ReportStreamingStructuralBreakFrom(
        string segment,
        TDocument document,
        long trustedSequence,
        ref long highestSoFar,
        List<AuditChainBreak> breaks)
    {
        if (document.Sequence <= trustedSequence)
        {
            breaks.Add(new AuditChainBreak(
                document.Sequence,
                AuditChainBreakKind.Duplicate,
                $"Sequence {document.Sequence} of segment '{segment}' was already covered by the trusted checkpoint at sequence {trustedSequence}."));

            return;
        }

        if (document.Sequence <= highestSoFar)
        {
            breaks.Add(new AuditChainBreak(
                document.Sequence,
                AuditChainBreakKind.Duplicate,
                $"Sequence {document.Sequence} appears more than once in segment '{segment}'."));

            return;
        }

        for (var missing = highestSoFar + 1; missing < document.Sequence; missing++)
        {
            breaks.Add(new AuditChainBreak(
                missing,
                AuditChainBreakKind.Gap,
                $"Sequence {missing} is missing from segment '{segment}'."));
        }

        highestSoFar = document.Sequence;
    }

    /// <summary>
    ///     Reads the latest checkpoint of the segment from every configured source, in registration order.
    /// </summary>
    /// <param name="segment">The segment under verification.</param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    /// <returns>The checkpoint each source holds, with unreachable sources flagged.</returns>
    private async ValueTask<IReadOnlyList<LoadedCheckpoint>> LoadSourceCheckpointsAsync(
        string segment,
        CancellationToken cancellationToken)
    {
        var sources = ResolveSources();
        List<LoadedCheckpoint> loaded = new(sources.Count);

        foreach (var source in sources)
        {
            var sourceName = source.GetType().Name;

#pragma warning disable CA1031 // A checkpoint source is an external anchor call that can fail in any way; the failure is logged with the source and segment and reported as an unreachable comparison.
            try
            {
                var checkpoint = await source.GetLatestAsync(segment, cancellationToken).ConfigureAwait(false);

                loaded.Add(new LoadedCheckpoint(sourceName, checkpoint, false));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                AuditVerifierLogMessages.SourceUnreachable(_logger, sourceName, segment, exception);
                loaded.Add(new LoadedCheckpoint(sourceName, null, true));
            }
#pragma warning restore CA1031
        }

        return loaded;
    }

    /// <summary>
    ///     Resolves every configured checkpoint source in registration order, with the store's own source first.
    /// </summary>
    /// <returns>The sources verification compares against.</returns>
    private List<ICheckpointSource> ResolveSources()
    {
        if (_chainOptions.SourceTypes.Count == 0)
        {
            return [];
        }

        List<ICheckpointSource> sources = new(_chainOptions.SourceTypes.Count);

        foreach (var sourceType in _chainOptions.SourceTypes)
        {
            sources.Add(ResolveService<ICheckpointSource>(sourceType, "checkpoint source"));
        }

        return sources;
    }

    /// <summary>
    ///     Resolves the checkpoint policy the chain cuts by, from the configured instance or type, or the
    ///     record-count default the checkpoint lifecycle also falls back to.
    /// </summary>
    /// <returns>The policy that decides whether a segment should already hold a checkpoint.</returns>
    private ICheckpointPolicy ResolveCheckpointPolicy()
    {
        if (_chainOptions.CheckpointPolicy is not null)
        {
            return _chainOptions.CheckpointPolicy;
        }

        if (_chainOptions.CheckpointPolicyType is null)
        {
            return CheckpointPolicies.EveryRecords(100);
        }

        return ResolveService<ICheckpointPolicy>(_chainOptions.CheckpointPolicyType, "checkpoint policy");
    }

    /// <summary>
    ///     Resolves every configured checkpoint signer in registration order.
    /// </summary>
    /// <returns>The signers that verify every compared checkpoint.</returns>
    private List<ICheckpointSigner> ResolveSigners()
    {
        if (_chainOptions.SignerTypes.Count == 0)
        {
            return [];
        }

        List<ICheckpointSigner> signers = new(_chainOptions.SignerTypes.Count);

        foreach (var signerType in _chainOptions.SignerTypes)
        {
            signers.Add(ResolveService<ICheckpointSigner>(signerType, "checkpoint signer"));
        }

        return signers;
    }

    /// <summary>
    ///     Resolves a chain piece the builder named as a type from the service provider.
    /// </summary>
    /// <typeparam name="TService">The piece contract.</typeparam>
    /// <param name="implementationType">The implementation type the builder named.</param>
    /// <param name="pieceName">The piece name used in the error when no provider is available.</param>
    /// <returns>The resolved piece.</returns>
    private TService ResolveService<TService>(Type implementationType, string pieceName)
        where TService : class
    {
        if (_serviceProvider is null)
        {
            throw new InvalidOperationException(
                $"The {pieceName} was registered as the type '{implementationType.FullName}' but no service provider " +
                "is available. Register the piece as an instance or pass an IServiceProvider to the chain.");
        }

        var service = _serviceProvider.GetService(implementationType);

        if (service is null)
        {
            throw new LiteBusDependencyResolutionException(implementationType);
        }

        return (TService) service;
    }

    /// <summary>
    ///     The checkpoint one source holds for the segment under verification.
    /// </summary>
    /// <param name="SourceName">The name of the checkpoint source read.</param>
    /// <param name="Checkpoint">The latest checkpoint the source holds, or <see langword="null" /> when it holds none or could not be reached.</param>
    /// <param name="Unreachable">Whether the source could not be reached.</param>
    private sealed record LoadedCheckpoint(string SourceName, AuditCheckpoint? Checkpoint, bool Unreachable);

    /// <summary>
    ///     The result of streaming the segment once: breaks, head, comparisons, and the stored checkpoint comparison.
    /// </summary>
    /// <param name="Breaks">The breaks collected so far, still open for unlinked, unpublished, stale, and missing breaks.</param>
    /// <param name="RecordCount">The number of documents the pass read.</param>
    /// <param name="HeadSequence">The sequence of the last document read, or the trusted sequence for an empty suffix.</param>
    /// <param name="HeadHash">The hash of the last document read, or the trusted hash for an empty suffix.</param>
    /// <param name="Comparisons">One comparison per configured source, in registration order.</param>
    /// <param name="StoredCheckpoint">The checkpoint the first source holds, or <see langword="null" /> when it holds none.</param>
    /// <param name="MatchesStoredCheckpoint">Whether the chain matches the stored checkpoint, or <see langword="null" /> when there is no stored checkpoint to compare.</param>
    /// <param name="StoredSignatureValid">Whether the stored checkpoint signatures verify, or <see langword="null" /> when there is no stored checkpoint or no signer is configured.</param>
    private sealed record ChainPass(
        List<AuditChainBreak> Breaks,
        long RecordCount,
        long? HeadSequence,
        string? HeadHash,
        List<AuditCheckpointComparison> Comparisons,
        AuditCheckpoint? StoredCheckpoint,
        bool? MatchesStoredCheckpoint,
        bool? StoredSignatureValid);
}
