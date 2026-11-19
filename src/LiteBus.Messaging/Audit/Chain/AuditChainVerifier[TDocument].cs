using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Messaging.Audit;

/// <summary>
///     Verifies one audit chain segment without any storage, so every store reuses the same pass.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     <para>
///         The verifier is pure: the caller reads the ordered documents of one segment and its latest checkpoint, and
///         the verifier recomputes every hash with the same canonical form, link rule, and hasher the writer used. A
///         scheme is picked per document by the name the document stores, so a segment that changed its algorithm, key,
///         or canonical form partway through still verifies, as long as every named scheme is in the lookup.
///     </para>
///     <para>
///         Unlike a first-break search, verification collects every break: missing and repeated sequences, documents out
///         of order, predecessor links that do not match the hash stored before them, hashes that do not match their
///         recomputation, unknown schemes, and checkpoint hash, count, and signature comparisons. A gap also breaks
///         the positional link of the document after it, which is reported beside the gap.
///     </para>
///     <para>
///         The documents must be the whole segment in ascending sequence order. A partial page reports its missing head
///         as gaps, and an unordered list reports the misplaced documents as duplicates.
///     </para>
/// </remarks>
public sealed class AuditChainVerifier<TDocument>
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     The hash schemes by the name documents store. Copied at construction and never mutated afterwards.
    /// </summary>
    private readonly Dictionary<string, AuditHashScheme<TDocument>> _schemesByName;

    /// <summary>
    ///     The signer checkpoint signatures are verified with, or <see langword="null" /> when the chain is unsigned.
    ///     Held for the life of the verifier.
    /// </summary>
    private readonly ICheckpointSigner? _signer;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditChainVerifier{TDocument}" /> class.
    /// </summary>
    /// <param name="schemesByName">The accepted hash schemes by the name documents store.</param>
    /// <param name="signer">
    ///     The signer checkpoint signatures are verified with, or <see langword="null" /> when the chain cuts unsigned
    ///     checkpoints.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="schemesByName" /> holds a <see langword="null" /> scheme.</exception>
    public AuditChainVerifier(
        IReadOnlyDictionary<string, AuditHashScheme<TDocument>> schemesByName,
        ICheckpointSigner? signer = null)
    {
        ArgumentNullException.ThrowIfNull(schemesByName);

        foreach (var pair in schemesByName)
        {
            if (pair.Value is null)
            {
                throw new ArgumentException(
                    $"The hash scheme '{pair.Key}' must not be null.", nameof(schemesByName));
            }
        }

        _schemesByName = new Dictionary<string, AuditHashScheme<TDocument>>(schemesByName, StringComparer.Ordinal);
        _signer = signer;
    }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditChainVerifier{TDocument}" /> class.
    /// </summary>
    /// <param name="schemes">The accepted hash schemes. Each scheme name may appear once.</param>
    /// <param name="signer">
    ///     The signer checkpoint signatures are verified with, or <see langword="null" /> when the chain cuts unsigned
    ///     checkpoints.
    /// </param>
    /// <exception cref="ArgumentException">
    ///     <paramref name="schemes" /> contains a <see langword="null" /> entry or lists a scheme name twice.
    /// </exception>
    public AuditChainVerifier(
        IEnumerable<AuditHashScheme<TDocument>> schemes,
        ICheckpointSigner? signer = null)
    {
        ArgumentNullException.ThrowIfNull(schemes);

        var lookup = new Dictionary<string, AuditHashScheme<TDocument>>(StringComparer.Ordinal);

        foreach (var scheme in schemes)
        {
            if (scheme is null)
            {
                throw new ArgumentException("The hash schemes must not contain a null entry.", nameof(schemes));
            }

            if (!lookup.TryAdd(scheme.Name, scheme))
            {
                throw new ArgumentException(
                    $"The hash scheme '{scheme.Name}' is listed twice. Each scheme name may appear once.",
                    nameof(schemes));
            }
        }

        _schemesByName = lookup;
        _signer = signer;
    }

    /// <summary>
    ///     Gets the accepted hash schemes by the name documents store.
    /// </summary>
    public IReadOnlyDictionary<string, AuditHashScheme<TDocument>> SchemesByName => _schemesByName;

    /// <summary>
    ///     Gets the signer checkpoint signatures are verified with, or <see langword="null" /> when the chain is
    ///     unsigned.
    /// </summary>
    public ICheckpointSigner? Signer => _signer;

    /// <summary>
    ///     Verifies one segment and compares it against its latest checkpoint.
    /// </summary>
    /// <param name="segment">The segment the documents were read from.</param>
    /// <param name="documents">The whole segment in ascending sequence order.</param>
    /// <param name="latestCheckpoint">
    ///     The latest checkpoint cut for the segment, or <see langword="null" /> when none was cut.
    /// </param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    /// <returns>Every break found, the head, and the checkpoint comparison.</returns>
    public async Task<AuditChainVerification> VerifyAsync(
        string segment,
        IReadOnlyList<TDocument> documents,
        AuditCheckpoint? latestCheckpoint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(documents);

        List<AuditChainBreak> breaks = [];

        ReportStructuralBreaks(segment, documents, breaks);
        await VerifyLinksAndHashesAsync(segment, documents, breaks, cancellationToken).ConfigureAwait(false);

        bool? matchesLatestCheckpoint = null;
        bool? checkpointSignatureValid = null;

        if (latestCheckpoint is not null)
        {
            matchesLatestCheckpoint = ReportCheckpointBreaks(segment, documents, latestCheckpoint, breaks);

            if (_signer is not null)
            {
                checkpointSignatureValid = await _signer
                    .VerifyAsync(latestCheckpoint, cancellationToken)
                    .ConfigureAwait(false);

                if (!checkpointSignatureValid.Value)
                {
                    breaks.Add(new AuditChainBreak(
                        latestCheckpoint.Sequence,
                        AuditChainBreakKind.CheckpointSignatureInvalid,
                        $"The signature on the checkpoint at sequence {latestCheckpoint.Sequence} of segment '{segment}' is missing or invalid."));
                }
            }
        }

        breaks.Sort(static (left, right) =>
            left.Sequence != right.Sequence
                ? left.Sequence.CompareTo(right.Sequence)
                : left.Kind.CompareTo(right.Kind));

        return new AuditChainVerification
        {
            Segment = segment,
            RecordCount = documents.Count,
            HeadSequence = documents.Count == 0 ? null : documents[documents.Count - 1].Sequence,
            HeadHash = documents.Count == 0 ? null : documents[documents.Count - 1].Hash,
            Breaks = [.. breaks],
            LatestCheckpoint = latestCheckpoint,
            MatchesLatestCheckpoint = matchesLatestCheckpoint,
            CheckpointSignatureValid = checkpointSignatureValid
        };
    }

    /// <summary>
    ///     Reports the breaks that need no hashing: missing, repeated, and misplaced sequences.
    /// </summary>
    /// <param name="segment">The segment under verification, named in each break.</param>
    /// <param name="documents">The documents read from the segment.</param>
    /// <param name="breaks">The breaks collected so far.</param>
    /// <remarks>
    ///     Gaps and repetitions are read off the set of sequences, so a document that arrives late is out of order
    ///     rather than missing: the set holds it. A sequence below one is never valid, because chain sequences start
    ///     at one.
    /// </remarks>
    private static void ReportStructuralBreaks(
        string segment,
        IReadOnlyList<TDocument> documents,
        List<AuditChainBreak> breaks)
    {
        Dictionary<long, int> counts = [];
        var maxSequence = 0L;

        foreach (var document in documents)
        {
            counts.TryGetValue(document.Sequence, out var seen);
            counts[document.Sequence] = seen + 1;

            if (document.Sequence > maxSequence)
            {
                maxSequence = document.Sequence;
            }
        }

        for (var missing = 1L; missing <= maxSequence; missing++)
        {
            if (!counts.ContainsKey(missing))
            {
                breaks.Add(new AuditChainBreak(
                    missing,
                    AuditChainBreakKind.Gap,
                    $"Sequence {missing} is missing from segment '{segment}'."));
            }
        }

        foreach (var pair in counts)
        {
            for (var extra = 1; extra < pair.Value; extra++)
            {
                breaks.Add(new AuditChainBreak(
                    pair.Key,
                    AuditChainBreakKind.Duplicate,
                    $"Sequence {pair.Key} appears {pair.Value} times in segment '{segment}'."));
            }
        }

        var highestSoFar = 0L;
        HashSet<long> orderReported = [];

        foreach (var document in documents)
        {
            if (document.Sequence < 1)
            {
                if (orderReported.Add(document.Sequence))
                {
                    breaks.Add(new AuditChainBreak(
                        document.Sequence,
                        AuditChainBreakKind.Duplicate,
                        $"Sequence {document.Sequence} in segment '{segment}' is not a valid chain position. Chain sequences start at one."));
                }

                continue;
            }

            if (document.Sequence < highestSoFar
                && counts[document.Sequence] == 1
                && orderReported.Add(document.Sequence))
            {
                breaks.Add(new AuditChainBreak(
                    document.Sequence,
                    AuditChainBreakKind.Duplicate,
                    $"Sequence {document.Sequence} is out of order in segment '{segment}'."));
            }

            if (document.Sequence > highestSoFar)
            {
                highestSoFar = document.Sequence;
            }
        }
    }

    /// <summary>
    ///     Walks the segment in order, checking each predecessor link and recomputing each hash.
    /// </summary>
    /// <param name="segment">The segment under verification, named in each break.</param>
    /// <param name="documents">The documents read from the segment, in ascending sequence order.</param>
    /// <param name="breaks">The breaks collected so far.</param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    /// <returns>A task representing the asynchronous verification.</returns>
    /// <remarks>
    ///     The link of a document is compared against the hash stored before it, so a gap breaks the positional link
    ///     of the document after it beside the gap itself. The hash is recomputed with the scheme the document names;
    ///     a document naming no scheme, or one outside the lookup, is reported without a recomputation.
    /// </remarks>
    private async Task VerifyLinksAndHashesAsync(
        string segment,
        IReadOnlyList<TDocument> documents,
        List<AuditChainBreak> breaks,
        CancellationToken cancellationToken)
    {
        string? previousHash = null;

        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!string.Equals(document.PreviousHash, previousHash, StringComparison.Ordinal))
            {
                breaks.Add(new AuditChainBreak(
                    document.Sequence,
                    AuditChainBreakKind.PreviousHashMismatch,
                    $"Sequence {document.Sequence} of segment '{segment}' carries a previous hash that does not match the hash stored before it."));
            }

            if (document.HashScheme is null || !_schemesByName.TryGetValue(document.HashScheme, out var scheme))
            {
                breaks.Add(new AuditChainBreak(
                    document.Sequence,
                    AuditChainBreakKind.UnknownHashScheme,
                    document.HashScheme is null
                        ? $"Sequence {document.Sequence} of segment '{segment}' names no hash scheme."
                        : $"Sequence {document.Sequence} of segment '{segment}' names the hash scheme '{document.HashScheme}', which is not in the lookup."));
            }
            else
            {
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

            previousHash = document.Hash;
        }
    }

    /// <summary>
    ///     Compares the segment against its latest checkpoint: the hash at the checkpoint sequence and the covered
    ///     count.
    /// </summary>
    /// <param name="segment">The segment under verification, named in each break.</param>
    /// <param name="documents">The documents read from the segment.</param>
    /// <param name="checkpoint">The latest checkpoint cut for the segment.</param>
    /// <param name="breaks">The breaks collected so far.</param>
    /// <returns>
    ///     <see langword="true" /> when the checkpoint hash and count both match; otherwise,
    ///     <see langword="false" />.
    /// </returns>
    /// <remarks>
    ///     The count comparison proves coverage: the segment must still hold every document the checkpoint counted up
    ///     to its sequence. A segment holding more, through documents appended past the checkpoint, still matches.
    /// </remarks>
    private static bool ReportCheckpointBreaks(
        string segment,
        IReadOnlyList<TDocument> documents,
        AuditCheckpoint checkpoint,
        List<AuditChainBreak> breaks)
    {
        var matches = true;
        TDocument? covered = null;
        var coveredCount = 0L;

        foreach (var document in documents)
        {
            if (document.Sequence <= checkpoint.Sequence)
            {
                coveredCount++;
            }

            if (document.Sequence == checkpoint.Sequence && covered is null)
            {
                covered = document;
            }
        }

        if (covered is null)
        {
            matches = false;
            breaks.Add(new AuditChainBreak(
                checkpoint.Sequence,
                AuditChainBreakKind.CheckpointHashMismatch,
                $"The checkpoint at sequence {checkpoint.Sequence} of segment '{segment}' names a hash, but the segment holds no document at that sequence."));
        }
        else if (!string.Equals(covered.Hash, checkpoint.Hash, StringComparison.Ordinal))
        {
            matches = false;
            breaks.Add(new AuditChainBreak(
                checkpoint.Sequence,
                AuditChainBreakKind.CheckpointHashMismatch,
                $"The hash at sequence {checkpoint.Sequence} of segment '{segment}' does not match the checkpoint hash. The segment was rewritten after the checkpoint was cut."));
        }

        if (coveredCount < checkpoint.Count)
        {
            matches = false;
            breaks.Add(new AuditChainBreak(
                checkpoint.Sequence,
                AuditChainBreakKind.CheckpointCountMismatch,
                $"Segment '{segment}' holds {coveredCount} documents up to sequence {checkpoint.Sequence}, but the checkpoint counted {checkpoint.Count}. A document the checkpoint covered is missing."));
        }

        return matches;
    }
}
