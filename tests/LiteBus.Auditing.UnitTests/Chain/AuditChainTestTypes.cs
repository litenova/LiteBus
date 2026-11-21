using System.Globalization;
using LiteBus.Auditing.Abstractions;
using System.Security.Cryptography;
using System.Text;
using LiteBus.Auditing.Checkpoints;
using LiteBus.Auditing.Verification;
using LiteBus.Auditing.Hashing;

namespace LiteBus.Auditing.UnitTests.Chain;

/// <summary>
///     A stored audit document with one business field, standing in for an application-owned document type.
/// </summary>
internal sealed class ChainTestDocument : IAuditChainDocument
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
///     The canonical form of <see cref="ChainTestDocument" />: every field except the hash itself.
/// </summary>
/// <remarks>
///     The bytes include the segment, the sequence, and the previous hash, which is what lets the default
///     <see cref="PreviousHashLinkRule" /> pass them through unchanged. The scheme name stays out, so renaming a
///     scheme never breaks the documents written with it.
/// </remarks>
internal sealed class ChainTestCanonicalizer : IAuditCanonicalizer<ChainTestDocument>
{
    /// <inheritdoc />
    public byte[] Canonicalize(ChainTestDocument document)
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
internal sealed class ChainTestSigner : ICheckpointSigner
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
    ///     Initializes a new instance of the <see cref="ChainTestSigner" /> class.
    /// </summary>
    /// <param name="key">The key the test signatures are computed with.</param>
    public ChainTestSigner(byte[] key)
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
///     Builds chained test documents and checkpoints with the same steps a storage adapter runs at write time.
/// </summary>
internal static class AuditChainTestData
{
    /// <summary>
    ///     Builds a hash scheme over the test canonical form and the default link rule.
    /// </summary>
    /// <param name="name">The scheme name stored on documents.</param>
    /// <param name="hasher">The hasher, or <see langword="null" /> for plain SHA-256.</param>
    /// <returns>The scheme.</returns>
    public static AuditHashScheme<ChainTestDocument> TestScheme(string name, IAuditHasher? hasher = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new AuditHashScheme<ChainTestDocument>(
            name,
            new ChainTestCanonicalizer(),
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
    public static async Task<List<ChainTestDocument>> BuildChainAsync(
        string segment,
        IReadOnlyList<AuditHashScheme<ChainTestDocument>> schemes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(schemes);

        var documents = new List<ChainTestDocument>(schemes.Count);
        string? previousHash = null;

        for (var index = 0; index < schemes.Count; index++)
        {
            var scheme = schemes[index];
            var document = new ChainTestDocument
            {
                Segment = segment,
                Sequence = index + 1,
                PreviousHash = previousHash,
                HashScheme = scheme.Name,
                Action = string.Concat("orders.step-", (index + 1).ToString(CultureInfo.InvariantCulture))
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
    public static Task<List<ChainTestDocument>> BuildChainAsync(
        string segment,
        AuditHashScheme<ChainTestDocument> scheme,
        int count,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        var schemes = new AuditHashScheme<ChainTestDocument>[count];

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
        ChainTestDocument document,
        AuditHashScheme<ChainTestDocument> scheme,
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
    /// <returns>The checkpoint covering the head.</returns>
    public static AuditCheckpoint CheckpointFor(string segment, IReadOnlyList<ChainTestDocument> documents)
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
            DateTimeOffset.UtcNow);
    }
}
