using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using JasperFx;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Audit;
using LiteBus.Runtime.Abstractions.Exceptions;
using Marten;
using Marten.Exceptions;
using Npgsql;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     The typed hash chain operations for one stored audit document type.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     Built by <see cref="MartenAuditHashChainBuilder" />. Every operation keeps the segment head with optimistic
///     concurrency: a lost race retries against the winner, so two records never share a sequence number and a
///     checkpoint always covers a committed head.
/// </remarks>
internal sealed class MartenAuditChainConfiguration<TDocument> : MartenAuditChainConfiguration
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Gets the configured record mapper instance, when one was given.
    /// </summary>
    private readonly IAuditRecordMapper<TDocument>? _mapper;

    /// <summary>
    ///     Gets the configured record mapper type, when one was given instead of an instance.
    /// </summary>
    private readonly Type? _mapperType;

    /// <summary>
    ///     Gets the configured segmenter instance, when one was given.
    /// </summary>
    private readonly IAuditSegmenter<TDocument>? _segmenter;

    /// <summary>
    ///     Gets the configured segmenter type, when one was given instead of an instance.
    /// </summary>
    private readonly Type? _segmenterType;

    /// <summary>
    ///     Gets the scheme newly appended records are written with, when one was given as an instance.
    /// </summary>
    private readonly AuditHashScheme<TDocument>? _writeScheme;

    /// <summary>
    ///     Gets the factory that builds the write scheme from container services, when one was given instead of an
    ///     instance.
    /// </summary>
    private readonly Func<IServiceProvider, AuditHashScheme<TDocument>>? _writeSchemeFactory;

    /// <summary>
    ///     Gets the schemes accepted for verification, including the write scheme when it was given as an instance.
    /// </summary>
    private readonly IReadOnlyList<AuditHashScheme<TDocument>> _acceptedSchemes;

    /// <summary>
    ///     Gets the factories that build accepted schemes from container services.
    /// </summary>
    private readonly IReadOnlyList<Func<IServiceProvider, AuditHashScheme<TDocument>>> _acceptedSchemeFactories;

    /// <summary>
    ///     Gets the policy that decides when a segment is due for a checkpoint.
    /// </summary>
    private readonly ICheckpointPolicy _policy;

    /// <summary>
    ///     Gets the configured checkpoint signer instance, when one was given.
    /// </summary>
    private readonly ICheckpointSigner? _signer;

    /// <summary>
    ///     Gets the configured checkpoint signer type, when one was given instead of an instance.
    /// </summary>
    private readonly Type? _signerType;

    /// <summary>
    ///     Gets the configured checkpoint publisher instance, when one was given.
    /// </summary>
    private readonly ICheckpointPublisher? _publisher;

    /// <summary>
    ///     Gets the configured checkpoint publisher type, when one was given instead of an instance.
    /// </summary>
    private readonly Type? _publisherType;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditChainConfiguration{TDocument}" /> class.
    /// </summary>
    /// <param name="mapper">The configured record mapper instance, when one was given.</param>
    /// <param name="mapperType">The configured record mapper type, when one was given instead of an instance.</param>
    /// <param name="segmenter">The configured segmenter instance, when one was given.</param>
    /// <param name="segmenterType">The configured segmenter type, when one was given instead of an instance.</param>
    /// <param name="writeScheme">The scheme newly appended records are written with, when one was given as an instance.</param>
    /// <param name="writeSchemeFactory">The factory that builds the write scheme, when one was given instead of an instance.</param>
    /// <param name="acceptedSchemes">The schemes accepted for verification, including the write scheme when it was given as an instance.</param>
    /// <param name="acceptedSchemeFactories">The factories that build accepted schemes from container services.</param>
    /// <param name="policy">The policy that decides when a segment is due for a checkpoint.</param>
    /// <param name="signer">The configured checkpoint signer instance, when one was given.</param>
    /// <param name="signerType">The configured checkpoint signer type, when one was given instead of an instance.</param>
    /// <param name="publisher">The configured checkpoint publisher instance, when one was given.</param>
    /// <param name="publisherType">The configured checkpoint publisher type, when one was given instead of an instance.</param>
    internal MartenAuditChainConfiguration(
        IAuditRecordMapper<TDocument>? mapper,
        Type? mapperType,
        IAuditSegmenter<TDocument>? segmenter,
        Type? segmenterType,
        AuditHashScheme<TDocument>? writeScheme,
        Func<IServiceProvider, AuditHashScheme<TDocument>>? writeSchemeFactory,
        IReadOnlyList<AuditHashScheme<TDocument>> acceptedSchemes,
        IReadOnlyList<Func<IServiceProvider, AuditHashScheme<TDocument>>> acceptedSchemeFactories,
        ICheckpointPolicy policy,
        ICheckpointSigner? signer,
        Type? signerType,
        ICheckpointPublisher? publisher,
        Type? publisherType)
    {
        ArgumentNullException.ThrowIfNull(acceptedSchemes);
        ArgumentNullException.ThrowIfNull(acceptedSchemeFactories);
        ArgumentNullException.ThrowIfNull(policy);

        if (writeScheme is null && writeSchemeFactory is null)
        {
            throw new InvalidOperationException("No hash scheme is configured for the chain.");
        }

        _mapper = mapper;
        _mapperType = mapperType;
        _segmenter = segmenter;
        _segmenterType = segmenterType;
        _writeScheme = writeScheme;
        _writeSchemeFactory = writeSchemeFactory;
        _acceptedSchemes = acceptedSchemes;
        _acceptedSchemeFactories = acceptedSchemeFactories;
        _policy = policy;
        _signer = signer;
        _signerType = signerType;
        _publisher = publisher;
        _publisherType = publisherType;
    }

    /// <inheritdoc />
    internal override Type DocumentType => typeof(TDocument);

    /// <inheritdoc />
    internal override ICheckpointPolicy CheckpointPolicy => _policy;

    /// <inheritdoc />
    /// <remarks>
    ///     A lost append race retries against the winner: a concurrent append fails with an optimistic concurrency
    ///     conflict on the segment head, and a head creation race fails with a unique violation on the head identity.
    ///     The loop ends when a session commits, and the cancellation check keeps a cancelled mediation from
    ///     appending forever under contention.
    /// </remarks>
    internal override async Task AppendAsync(
        IDocumentStore store,
        AuditRecord record,
        TimeProvider timeProvider,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var mapper = ResolveMapper(serviceProvider);
        var segmenter = ResolveSegmenter(serviceProvider);
        var writeScheme = ResolveWriteScheme(serviceProvider);
        var document = mapper.Map(record);
        var segment = segmenter.SegmentOf(document);

        if (string.IsNullOrWhiteSpace(segment))
        {
            throw new InvalidOperationException(
                $"The audit segmenter returned no segment for action '{record.Action}'. " +
                "Return a stable segment name from the segmenter passed to SegmentBy.");
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var session = store.LightweightSession(IsolationLevel.ReadCommitted);

                await using (session.ConfigureAwait(false))
                {
                    var head = await session
                        .LoadAsync<MartenAuditSegmentHead>(segment, cancellationToken)
                        .ConfigureAwait(false);

                    if (head is not null && head.Expired)
                    {
                        throw new InvalidOperationException(
                            $"The audit segment '{segment}' has expired and no longer accepts records.");
                    }

                    List<TDocument> stored = [];
                    var previousHash = head?.LastHash;
                    var sequence = (head?.LastSequence ?? 0) + 1;

                    if (head is null)
                    {
                        var genesis = segmenter.CreateGenesis(segment, document);

                        if (genesis is not null)
                        {
                            genesis.Segment = segment;
                            genesis.Sequence = sequence;
                            genesis.PreviousHash = null;
                            genesis.HashScheme = writeScheme.Name;
                            genesis.Hash = await HashDocumentAsync(
                                    writeScheme,
                                    genesis,
                                    segment,
                                    sequence,
                                    null,
                                    cancellationToken)
                                .ConfigureAwait(false);

                            stored.Add(genesis);
                            previousHash = genesis.Hash;
                            sequence++;
                        }
                    }

                    document.Segment = segment;
                    document.Sequence = sequence;
                    document.PreviousHash = previousHash;
                    document.HashScheme = writeScheme.Name;
                    document.Hash = await HashDocumentAsync(
                            writeScheme,
                            document,
                            segment,
                            sequence,
                            previousHash,
                            cancellationToken)
                        .ConfigureAwait(false);

                    stored.Add(document);

                    // The loaded head is updated in place, never replaced: the update asserts the version captured
                    // at load, which is the check that serializes concurrent appends.
                    head ??= new MartenAuditSegmentHead { Id = segment };

                    if (head.Count == 0 && head.LastSequence > 0)
                    {
                        // Heads written before the count existed carry zero; their segments were written without
                        // gaps, so the last sequence is the count.
                        head.Count = head.LastSequence;
                    }

                    head.LastSequence = sequence;
                    head.LastHash = document.Hash;
                    head.HashScheme = writeScheme.Name;
                    head.Count += stored.Count;

                    foreach (var storedDocument in stored)
                    {
                        session.Store(storedDocument);
                    }

                    session.Store(head);

                    await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    var candidate = new AuditCheckpointCandidate(
                        segment,
                        head.LastSequence,
                        head.Count,
                        head.LastCheckpointSequence,
                        head.LastCheckpointAt,
                        timeProvider.GetUtcNow());

                    if (_policy.IsDue(candidate))
                    {
                        await CutCheckpointAsync(
                                store,
                                segment,
                                AuditCheckpointKind.Regular,
                                timeProvider,
                                serviceProvider,
                                cancellationToken)
                            .ConfigureAwait(false);
                    }

                    return;
                }
            }
            catch (Exception exception) when (IsAppendConflict(exception))
            {
                // Another writer committed first; the next pass reads the winner.
            }
        }
    }

    /// <inheritdoc />
    internal override async Task<AuditChainVerification> VerifyAsync(
        IDocumentStore store,
        string segment,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var session = store.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var documents = await session
                .Query<TDocument>()
                .Where(SegmentEquals(segment))
                .OrderBy(SequenceKey())
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            foreach (var document in documents)
            {
                // Records written before schemes carry no name; they were hashed with the default scheme.
                document.HashScheme ??= MartenAuditHashSchemes.Sha256V1.Name;
            }

            var latest = await new MartenAuditCheckpointSource(store)
                .GetLatestAsync(segment, cancellationToken)
                .ConfigureAwait(false);

            return await VerifyWithCoreAsync(documents, segment, latest, serviceProvider, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     A concurrent append moves the head between the load and the commit; the cut then retries and covers the
    ///     newer head, because an explicit cut means the segment as it stands now.
    /// </remarks>
    internal override async Task<AuditCheckpoint> CutCheckpointAsync(
        IDocumentStore store,
        string segment,
        AuditCheckpointKind kind,
        TimeProvider timeProvider,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(timeProvider);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var session = store.LightweightSession(IsolationLevel.ReadCommitted);

                await using (session.ConfigureAwait(false))
                {
                    var head = await session
                        .LoadAsync<MartenAuditSegmentHead>(segment, cancellationToken)
                        .ConfigureAwait(false);

                    if (head is null || head.LastSequence == 0)
                    {
                        throw new InvalidOperationException(
                            $"The audit segment '{segment}' holds no records, so no checkpoint can be cut.");
                    }

                    if (head.Expired)
                    {
                        throw new InvalidOperationException(
                            $"The audit segment '{segment}' has expired, so no checkpoint can be cut.");
                    }

                    if (head.Count == 0)
                    {
                        // Heads written before the count existed carry zero; their segments were written without
                        // gaps, so the last sequence is the count.
                        head.Count = head.LastSequence;
                    }

                    var checkpoint = await BuildCheckpointAsync(
                            head,
                            kind,
                            timeProvider.GetUtcNow(),
                            serviceProvider,
                            cancellationToken)
                        .ConfigureAwait(false);

                    session.Store(MartenAuditCheckpoint.FromAuditCheckpoint(checkpoint));
                    head.LastCheckpointSequence = checkpoint.Sequence;
                    head.LastCheckpointAt = checkpoint.CreatedAt;
                    session.Store(head);

                    await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    await PublishCheckpointAsync(checkpoint, serviceProvider, cancellationToken).ConfigureAwait(false);

                    return checkpoint;
                }
            }
            catch (Exception exception) when (IsAppendConflict(exception))
            {
                // Another writer committed first; the next pass cuts from the newer head.
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Expiry runs as a restartable sequence: the final checkpoint is cut and stored first, then published,
    ///     and only then are the segment documents deleted and the head marked expired. A stored final checkpoint
    ///     is reused when one already exists, so publishing is keyed by the checkpoint identity and a retried
    ///     expiry republishes the same checkpoint before finishing the deletion. A call after the deletion returns
    ///     the stored final checkpoint without publishing again. Two expiries that overlap may publish the same
    ///     checkpoint identity twice, which the publisher deduplicates. Expiry expects a segment that receives no
    ///     new records while it runs, so the final checkpoint covers every document the deletion removes.
    /// </remarks>
    internal override async Task<AuditCheckpoint> ExpireSegmentAsync(
        IDocumentStore store,
        string segment,
        TimeProvider timeProvider,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(timeProvider);

        var completed = await FindCompletedFinalCheckpointAsync(store, segment, cancellationToken).ConfigureAwait(false);

        if (completed is not null)
        {
            return completed;
        }

        var checkpoint = await FindOrCutFinalCheckpointAsync(
                store,
                segment,
                timeProvider,
                serviceProvider,
                cancellationToken)
            .ConfigureAwait(false);

        await PublishCheckpointAsync(checkpoint, serviceProvider, cancellationToken).ConfigureAwait(false);
        await DeleteSegmentDocumentsAsync(store, segment, cancellationToken).ConfigureAwait(false);

        return checkpoint;
    }

    /// <summary>
    ///     Returns the stored final checkpoint of an expired segment, or <see langword="null" /> when the segment is
    ///     still open.
    /// </summary>
    /// <param name="store">The application-owned Marten document store.</param>
    /// <param name="segment">The segment to inspect.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The stored final checkpoint, or <see langword="null" /> when the segment is still open.</returns>
    private static async Task<AuditCheckpoint?> FindCompletedFinalCheckpointAsync(
        IDocumentStore store,
        string segment,
        CancellationToken cancellationToken)
    {
        var session = store.LightweightSession(IsolationLevel.ReadCommitted);

        await using (session.ConfigureAwait(false))
        {
            var head = await session
                .LoadAsync<MartenAuditSegmentHead>(segment, cancellationToken)
                .ConfigureAwait(false);

            if (head is null || !head.Expired)
            {
                return null;
            }

            var latest = await new MartenAuditCheckpointSource(store)
                .GetLatestAsync(segment, cancellationToken)
                .ConfigureAwait(false);

            if (latest is { Kind: AuditCheckpointKind.Final })
            {
                return latest;
            }

            throw new InvalidOperationException(
                $"The audit segment '{segment}' has expired but keeps no final checkpoint.");
        }
    }

    /// <summary>
    ///     Reuses the stored final checkpoint of a segment, or cuts and stores one when none exists yet.
    /// </summary>
    /// <param name="store">The application-owned Marten document store.</param>
    /// <param name="segment">The segment to expire.</param>
    /// <param name="timeProvider">The clock that stamps a newly cut final checkpoint.</param>
    /// <param name="serviceProvider">Resolves pieces the builder named as types, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The stored final checkpoint.</returns>
    /// <remarks>
    ///     A lost race retries against the winner: concurrent expiry attempts contend on the head update, and the
    ///     loser reuses the final checkpoint the winner stored.
    /// </remarks>
    private async Task<AuditCheckpoint> FindOrCutFinalCheckpointAsync(
        IDocumentStore store,
        string segment,
        TimeProvider timeProvider,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var session = store.LightweightSession(IsolationLevel.ReadCommitted);

                await using (session.ConfigureAwait(false))
                {
                    var head = await session
                        .LoadAsync<MartenAuditSegmentHead>(segment, cancellationToken)
                        .ConfigureAwait(false);

                    if (head is null || head.LastSequence == 0)
                    {
                        throw new InvalidOperationException(
                            $"The audit segment '{segment}' holds no records, so it cannot expire.");
                    }

                    if (head.Expired)
                    {
                        var completed = await FindStoredFinalCheckpointAsync(store, segment, cancellationToken)
                            .ConfigureAwait(false);

                        if (completed is null)
                        {
                            throw new InvalidOperationException(
                                $"The audit segment '{segment}' has expired but keeps no final checkpoint.");
                        }

                        return completed;
                    }

                    var stored = await FindStoredFinalCheckpointAsync(store, segment, cancellationToken)
                        .ConfigureAwait(false);

                    if (stored is not null)
                    {
                        return stored;
                    }

                    if (head.Count == 0)
                    {
                        head.Count = head.LastSequence;
                    }

                    var checkpoint = await BuildCheckpointAsync(
                            head,
                            AuditCheckpointKind.Final,
                            timeProvider.GetUtcNow(),
                            serviceProvider,
                            cancellationToken)
                        .ConfigureAwait(false);

                    session.Store(MartenAuditCheckpoint.FromAuditCheckpoint(checkpoint));
                    head.LastCheckpointSequence = checkpoint.Sequence;
                    head.LastCheckpointAt = checkpoint.CreatedAt;
                    session.Store(head);

                    await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    return checkpoint;
                }
            }
            catch (Exception exception) when (IsAppendConflict(exception))
            {
                // Another writer committed first; the next pass reuses the final checkpoint it stored, if any.
            }
        }
    }

    /// <summary>
    ///     Finds the stored final checkpoint of a segment, when one was cut.
    /// </summary>
    /// <param name="store">The application-owned Marten document store.</param>
    /// <param name="segment">The segment to inspect.</param>
    /// <param name="cancellationToken">The token used to cancel the read.</param>
    /// <returns>The stored final checkpoint, or <see langword="null" /> when none was cut yet.</returns>
    private static async Task<AuditCheckpoint?> FindStoredFinalCheckpointAsync(
        IDocumentStore store,
        string segment,
        CancellationToken cancellationToken)
    {
        var session = store.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var stored = await session
                .Query<MartenAuditCheckpoint>()
                .Where(checkpoint => checkpoint.Segment == segment && checkpoint.Kind == AuditCheckpointKind.Final)
                .OrderByDescending(checkpoint => checkpoint.Sequence)
                .ThenByDescending(checkpoint => checkpoint.CreatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return stored.Count == 0 ? null : stored[0].ToAuditCheckpoint();
        }
    }

    /// <summary>
    ///     Deletes every document of a segment and marks its head expired in one commit.
    /// </summary>
    /// <param name="store">The application-owned Marten document store.</param>
    /// <param name="segment">The segment to delete.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>A task representing the asynchronous deletion.</returns>
    /// <remarks>
    ///     A lost race retries against the winner. A head that is already expired means a concurrent expiry
    ///     finished the deletion, so there is nothing left to delete.
    /// </remarks>
    private static async Task DeleteSegmentDocumentsAsync(
        IDocumentStore store,
        string segment,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var session = store.LightweightSession(IsolationLevel.ReadCommitted);

                await using (session.ConfigureAwait(false))
                {
                    var head = await session
                        .LoadAsync<MartenAuditSegmentHead>(segment, cancellationToken)
                        .ConfigureAwait(false);

                    if (head is null)
                    {
                        throw new InvalidOperationException(
                            $"The audit segment '{segment}' lost its head after its final checkpoint was published.");
                    }

                    if (head.Expired)
                    {
                        return;
                    }

                    session.DeleteWhere<TDocument>(SegmentEquals(segment));
                    head.Expired = true;
                    session.Store(head);

                    await session.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                    return;
                }
            }
            catch (Exception exception) when (IsAppendConflict(exception))
            {
                // Another writer committed first; the next pass re-reads the head.
            }
        }
    }

    /// <summary>
    ///     Verifies ordered segment documents through the storage-free core verifier.
    /// </summary>
    /// <param name="documents">The segment documents in ascending sequence order.</param>
    /// <param name="segment">The segment the documents were read from.</param>
    /// <param name="latest">The latest stored checkpoint, or <see langword="null" /> when none was cut.</param>
    /// <param name="serviceProvider">Resolves pieces the builder named as types, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The token used to cancel the verification.</param>
    /// <returns>Every break found, the head, and the checkpoint comparison.</returns>
    private async Task<AuditChainVerification> VerifyWithCoreAsync(
        IReadOnlyList<TDocument> documents,
        string segment,
        AuditCheckpoint? latest,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        var schemes = ResolveSchemes(serviceProvider);
        var signer = ResolveSignerOrNull(serviceProvider);
        var verifier = new AuditChainVerifier<TDocument>(schemes, signer);

        return await verifier.VerifyAsync(segment, documents, latest, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Hashes one document with a scheme: canonical form, link rule, then the hasher.
    /// </summary>
    /// <param name="scheme">The scheme the hash is computed with.</param>
    /// <param name="document">The document with its chain fields assigned, except the hash itself.</param>
    /// <param name="segment">The segment the document is appended to.</param>
    /// <param name="sequence">The position of the document in the segment.</param>
    /// <param name="previousHash">The hash of the previous document, or <see langword="null" /> for the first one.</param>
    /// <param name="cancellationToken">The token used to cancel the hash.</param>
    /// <returns>The hash stored on the document.</returns>
    private static async Task<string> HashDocumentAsync(
        AuditHashScheme<TDocument> scheme,
        TDocument document,
        string segment,
        long sequence,
        string? previousHash,
        CancellationToken cancellationToken)
    {
        var canonical = scheme.Canonicalizer.Canonicalize(document);
        var linked = scheme.LinkRule.Link(new AuditLinkInput(segment, sequence, previousHash, canonical));

        return await scheme.Hasher.HashAsync(linked, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds and signs a checkpoint from a segment head.
    /// </summary>
    /// <param name="head">The head the checkpoint covers.</param>
    /// <param name="kind">Why the checkpoint is cut.</param>
    /// <param name="createdAt">When the checkpoint is cut.</param>
    /// <param name="serviceProvider">Resolves pieces the builder named as types, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The token used to cancel the signing.</param>
    /// <returns>The built checkpoint, signed when a signer is configured.</returns>
    private async Task<AuditCheckpoint> BuildCheckpointAsync(
        MartenAuditSegmentHead head,
        AuditCheckpointKind kind,
        DateTimeOffset createdAt,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        var headHash = head.LastHash;

        if (string.IsNullOrWhiteSpace(headHash))
        {
            throw new InvalidOperationException(
                $"The audit segment '{head.Id}' holds records but no head hash, so no checkpoint can be cut.");
        }

        var schemeName = string.IsNullOrWhiteSpace(head.HashScheme)
            ? MartenAuditHashSchemes.Sha256V1.Name
            : head.HashScheme;
        var checkpoint = new AuditCheckpoint(
            Guid.NewGuid(),
            head.Id,
            head.LastSequence,
            head.Count,
            headHash,
            schemeName,
            kind,
            createdAt);
        var signer = ResolveSignerOrNull(serviceProvider);

        if (signer is null)
        {
            return checkpoint;
        }

        var signature = await signer.SignAsync(checkpoint, cancellationToken).ConfigureAwait(false);

        return checkpoint with { Signature = signature };
    }

    /// <summary>
    ///     Publishes a checkpoint after it was stored, when a publisher is configured.
    /// </summary>
    /// <param name="checkpoint">The checkpoint that was cut.</param>
    /// <param name="serviceProvider">Resolves pieces the builder named as types, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The token used to cancel the publish.</param>
    /// <returns>A task representing the asynchronous publish.</returns>
    private async Task PublishCheckpointAsync(
        AuditCheckpoint checkpoint,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken)
    {
        var publisher = ResolvePublisherOrNull(serviceProvider);

        if (publisher is null)
        {
            return;
        }

        await publisher.PublishAsync(checkpoint, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Resolves the configured record mapper.
    /// </summary>
    /// <param name="serviceProvider">Resolves the mapper when the builder named it as a type.</param>
    /// <returns>The record mapper.</returns>
    private IAuditRecordMapper<TDocument> ResolveMapper(IServiceProvider? serviceProvider)
    {
        if (_mapper is not null)
        {
            return _mapper;
        }

        if (_mapperType is null)
        {
            throw new InvalidOperationException("No audit record mapper is configured for the chain.");
        }

        return ResolveService<IAuditRecordMapper<TDocument>>(_mapperType, serviceProvider, "audit record mapper");
    }

    /// <summary>
    ///     Resolves the configured segmenter.
    /// </summary>
    /// <param name="serviceProvider">Resolves the segmenter when the builder named it as a type.</param>
    /// <returns>The segmenter.</returns>
    private IAuditSegmenter<TDocument> ResolveSegmenter(IServiceProvider? serviceProvider)
    {
        if (_segmenter is not null)
        {
            return _segmenter;
        }

        if (_segmenterType is null)
        {
            throw new InvalidOperationException("No audit segmenter is configured for the chain.");
        }

        return ResolveService<IAuditSegmenter<TDocument>>(_segmenterType, serviceProvider, "audit segmenter");
    }

    /// <summary>
    ///     Resolves the scheme newly appended records are written with.
    /// </summary>
    /// <param name="serviceProvider">Builds the scheme when the builder was given a factory, or <see langword="null" />.</param>
    /// <returns>The write scheme.</returns>
    private AuditHashScheme<TDocument> ResolveWriteScheme(IServiceProvider? serviceProvider)
    {
        if (_writeScheme is not null)
        {
            return _writeScheme;
        }

        if (_writeSchemeFactory is null)
        {
            throw new InvalidOperationException("No hash scheme is configured for the chain.");
        }

        if (serviceProvider is null)
        {
            throw new InvalidOperationException(
                "The write hash scheme was registered as a factory but no service provider is available. " +
                "Pass an IServiceProvider when constructing the trail or the chain.");
        }

        var scheme = _writeSchemeFactory(serviceProvider);

        if (scheme is null)
        {
            throw new InvalidOperationException(
                "The write hash scheme factory returned no scheme. Return a scheme from the factory passed to UseScheme.");
        }

        return scheme;
    }

    /// <summary>
    ///     Resolves every scheme verification accepts, keyed by scheme name.
    /// </summary>
    /// <param name="serviceProvider">Builds the schemes the builder gave as factories, or <see langword="null" />.</param>
    /// <returns>The accepted schemes by name, including the write scheme.</returns>
    private Dictionary<string, AuditHashScheme<TDocument>> ResolveSchemes(IServiceProvider? serviceProvider)
    {
        List<AuditHashScheme<TDocument>> candidates = [ResolveWriteScheme(serviceProvider)];
        candidates.AddRange(_acceptedSchemes);

        foreach (var factory in _acceptedSchemeFactories)
        {
            if (serviceProvider is null)
            {
                throw new InvalidOperationException(
                    "An accepted hash scheme was registered as a factory but no service provider is available. " +
                    "Pass an IServiceProvider when constructing the trail or the chain.");
            }

            var scheme = factory(serviceProvider);

            if (scheme is null)
            {
                throw new InvalidOperationException(
                    "An accepted hash scheme factory returned no scheme. Return a scheme from the factory passed to AcceptScheme.");
            }

            candidates.Add(scheme);
        }

        var schemes = new Dictionary<string, AuditHashScheme<TDocument>>(StringComparer.Ordinal);

        foreach (var group in candidates.GroupBy(static scheme => scheme.Name, StringComparer.Ordinal))
        {
            var distinct = group.Distinct().ToList();

            if (distinct.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Two different hash schemes are named '{group.Key}'. Hash scheme names must be unique within a chain.");
            }

            schemes[group.Key] = distinct[0];
        }

        return schemes;
    }

    /// <summary>
    ///     Resolves the configured checkpoint signer, when one is configured.
    /// </summary>
    /// <param name="serviceProvider">Resolves the signer when the builder named it as a type.</param>
    /// <returns>The signer, or <see langword="null" /> when none is configured.</returns>
    private ICheckpointSigner? ResolveSignerOrNull(IServiceProvider? serviceProvider)
    {
        if (_signer is not null)
        {
            return _signer;
        }

        return _signerType is null
            ? null
            : ResolveService<ICheckpointSigner>(_signerType, serviceProvider, "checkpoint signer");
    }

    /// <summary>
    ///     Resolves the configured checkpoint publisher, when one is configured.
    /// </summary>
    /// <param name="serviceProvider">Resolves the publisher when the builder named it as a type.</param>
    /// <returns>The publisher, or <see langword="null" /> when none is configured.</returns>
    private ICheckpointPublisher? ResolvePublisherOrNull(IServiceProvider? serviceProvider)
    {
        if (_publisher is not null)
        {
            return _publisher;
        }

        return _publisherType is null
            ? null
            : ResolveService<ICheckpointPublisher>(_publisherType, serviceProvider, "checkpoint publisher");
    }

    /// <summary>
    ///     Resolves a chain piece the builder named as a type from the service provider.
    /// </summary>
    /// <typeparam name="TService">The piece contract.</typeparam>
    /// <param name="implementationType">The implementation type the builder named.</param>
    /// <param name="serviceProvider">The provider the piece is resolved from.</param>
    /// <param name="pieceName">The piece name used in the error when no provider is available.</param>
    /// <returns>The resolved piece.</returns>
    private static TService ResolveService<TService>(
        Type implementationType,
        IServiceProvider? serviceProvider,
        string pieceName)
        where TService : class
    {
        if (serviceProvider is null)
        {
            throw new InvalidOperationException(
                $"The {pieceName} was registered as the type '{implementationType.FullName}' but no service provider " +
                "is available. Pass an IServiceProvider when constructing the trail or the chain.");
        }

        var service = serviceProvider.GetService(implementationType);

        if (service is null)
        {
            throw new LiteBusDependencyResolutionException(implementationType);
        }

        return (TService) service;
    }

    /// <summary>
    ///     Builds a predicate matching the documents of one segment.
    /// </summary>
    /// <param name="segment">The segment to match.</param>
    /// <returns>The predicate over the stored document type.</returns>
    /// <remarks>
    ///     Built against the closed document type rather than written as a lambda over the type parameter, so the
    ///     query provider always sees the concrete segment member instead of the interface one.
    /// </remarks>
    private static Expression<Func<TDocument, bool>> SegmentEquals(string segment)
    {
        var document = Expression.Parameter(typeof(TDocument), "document");
        var property = Expression.Property(document, nameof(IAuditChainDocument.Segment));
        var body = Expression.Equal(property, Expression.Constant(segment));

        return Expression.Lambda<Func<TDocument, bool>>(body, document);
    }

    /// <summary>
    ///     Builds the sequence key that orders the documents of one segment.
    /// </summary>
    /// <returns>The key over the stored document type.</returns>
    /// <remarks>
    ///     Built against the closed document type rather than written as a lambda over the type parameter, so the
    ///     query provider always sees the concrete sequence member instead of the interface one.
    /// </remarks>
    private static Expression<Func<TDocument, long>> SequenceKey()
    {
        var document = Expression.Parameter(typeof(TDocument), "document");
        var property = Expression.Property(document, nameof(IAuditChainDocument.Sequence));

        return Expression.Lambda<Func<TDocument, long>>(property, document);
    }

    /// <summary>
    ///     Decides whether a failure a chain session raised is a lost race worth retrying.
    /// </summary>
    /// <param name="exception">The failure a chain session raised, whatever wrapper Marten put around it.</param>
    /// <returns><see langword="true" /> when the chain reports a concurrency or head creation conflict.</returns>
    private static bool IsAppendConflict(Exception exception)
    {
        foreach (var current in EnumerateExceptions(exception))
        {
            if (current is ConcurrencyException or ConcurrentUpdateException)
            {
                return true;
            }

            if (current is PostgresException postgres && IsHeadWriteState(postgres.SqlState))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Enumerates an exception, its inner exceptions, and every exception an <see cref="AggregateException" />
    ///     carries, depth first.
    /// </summary>
    /// <param name="exception">The exception to start from.</param>
    /// <returns>Every exception reachable from <paramref name="exception" />.</returns>
    /// <remarks>
    ///     A batched commit can report several failures in one <see cref="AggregateException" />, and its
    ///     <see cref="Exception.InnerException" /> is only the first of them, so walking that chain alone can miss
    ///     the storage error that matters.
    /// </remarks>
    private static IEnumerable<Exception> EnumerateExceptions(Exception exception)
    {
        var pending = new Stack<Exception>();
        pending.Push(exception);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            yield return current;

            if (current is AggregateException aggregate)
            {
                for (var index = aggregate.InnerExceptions.Count - 1; index >= 0; index--)
                {
                    pending.Push(aggregate.InnerExceptions[index]);
                }
            }
            else if (current.InnerException is { } inner)
            {
                pending.Push(inner);
            }
        }
    }

    /// <summary>
    ///     Decides whether a PostgreSQL state reports a lost head write race.
    /// </summary>
    /// <param name="sqlState">The five-character state the storage error reported.</param>
    /// <returns>
    ///     <see langword="true" /> for a unique violation (<c>23505</c>), a serialization failure (<c>40001</c>), or a
    ///     deadlock (<c>40P01</c>); otherwise, <see langword="false" />.
    /// </returns>
    private static bool IsHeadWriteState(string sqlState)
    {
        return sqlState is "23505" or "40001" or "40P01";
    }
}
