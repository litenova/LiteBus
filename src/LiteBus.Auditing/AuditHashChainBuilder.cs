using System;
using LiteBus.Auditing.Abstractions;
using System.Linq;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing;

/// <summary>
///     Configures the audit hash chain: the stored document, segmenting, the write scheme, checkpointing, signing,
///     publishing, and verification sources.
/// </summary>
/// <remarks>
///     Reached through <see cref="AuditingModuleBuilder.UseHashChain" />. Every piece has a default, so a chain that
///     needs nothing special writes no code beyond enabling it. A storage package supplies the default document,
///     mapper, and segmenter for its store; an explicit call here always wins over those defaults.
/// </remarks>
public sealed class AuditHashChainBuilder
{
    /// <summary>
    ///     The options this builder writes.
    /// </summary>
    private readonly AuditHashChainOptions _options;

    /// <summary>
    ///     Gets the auditing module builder this chain builder configures the chain on.
    /// </summary>
    /// <remarks>
    ///     Anchor packages reach <see cref="AuditingModuleBuilder.RegisterChildModule" /> through this property, so
    ///     an anchor extension registers its settings module as a child of the auditing module.
    /// </remarks>
    internal AuditingModuleBuilder Owner { get; }

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditHashChainBuilder" /> class.
    /// </summary>
    /// <param name="options">The options this builder writes.</param>
    /// <param name="owner">The auditing module builder this chain builder configures the chain on.</param>
    internal AuditHashChainBuilder(AuditHashChainOptions options, AuditingModuleBuilder owner)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(owner);
        _options = options;
        Owner = owner;
    }

    /// <summary>
    ///     Stores chained records as the given document type, mapped by a mapper resolved from the container.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <typeparam name="TMapper">The mapper implementation, constructed by the container when the chain runs.</typeparam>
    /// <returns>The current builder.</returns>
    public AuditHashChainBuilder StoreAs<TDocument, TMapper>()
        where TDocument : class, IAuditChainDocument
        where TMapper : class, IAuditRecordMapper<TDocument>
    {
        _options.DocumentType = typeof(TDocument);
        _options.MapperType = typeof(TMapper);

        return this;
    }

    /// <summary>
    ///     Assigns stored documents to chain segments with a segmenter resolved from the container.
    /// </summary>
    /// <typeparam name="TSegmenter">The segmenter implementation, constructed by the container when the chain runs.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The segmenter must handle the configured document type. The match is checked when the module builds,
    ///     because the document type is known only at configuration time.
    /// </remarks>
    public AuditHashChainBuilder SegmentBy<TSegmenter>()
        where TSegmenter : class
    {
        _options.SegmenterType = typeof(TSegmenter);

        return this;
    }

    /// <summary>
    ///     Hashes newly appended records with the named scheme.
    /// </summary>
    /// <param name="schemeName">The scheme new records are written with, resolved by name when the chain runs.</param>
    /// <returns>The current builder.</returns>
    public AuditHashChainBuilder UseScheme(string schemeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemeName);

        _options.SchemeName = schemeName;

        return this;
    }

    /// <summary>
    ///     Cuts a checkpoint whenever the given policy says a segment is due.
    /// </summary>
    /// <param name="policy">The policy that decides when a checkpoint is due.</param>
    /// <returns>The current builder.</returns>
    public AuditHashChainBuilder CheckpointWhen(ICheckpointPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        _options.CheckpointPolicy = policy;
        _options.CheckpointPolicyType = null;

        return this;
    }

    /// <summary>
    ///     Cuts a checkpoint whenever the given policy says a segment is due, resolving the policy from the
    ///     container.
    /// </summary>
    /// <typeparam name="TPolicy">The policy implementation, constructed by the container when the chain runs.</typeparam>
    /// <returns>The current builder.</returns>
    public AuditHashChainBuilder CheckpointWhen<TPolicy>()
        where TPolicy : class, ICheckpointPolicy
    {
        _options.CheckpointPolicy = null;
        _options.CheckpointPolicyType = typeof(TPolicy);

        return this;
    }

    /// <summary>
    ///     Signs every cut checkpoint with a signer resolved from the container.
    /// </summary>
    /// <typeparam name="TSigner">The signer implementation, constructed by the container when the chain runs.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Every registered signer signs every checkpoint, so a log signature, a timestamp, and witness
    ///     cosignatures coexist. Registering the same signer twice is a no-op.
    /// </remarks>
    public AuditHashChainBuilder SignCheckpointsWith<TSigner>()
        where TSigner : class, ICheckpointSigner
    {
        AddSignerType(typeof(TSigner));

        return this;
    }

    /// <summary>
    ///     Publishes every cut checkpoint to a publisher resolved from the container.
    /// </summary>
    /// <typeparam name="TPublisher">The publisher implementation, constructed by the container when the chain runs.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Every registered publisher receives every checkpoint. Registering the same publisher twice is a no-op.
    /// </remarks>
    public AuditHashChainBuilder PublishCheckpointsTo<TPublisher>()
        where TPublisher : class, ICheckpointPublisher
    {
        AddPublisherType(typeof(TPublisher));

        return this;
    }

    /// <summary>
    ///     Compares verification against an extra checkpoint source resolved from the container.
    /// </summary>
    /// <typeparam name="TSource">The source implementation, constructed by the container when the chain runs.</typeparam>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The store's own source is always first; every registered source adds one comparison. Registering the same
    ///     source twice is a no-op.
    /// </remarks>
    public AuditHashChainBuilder VerifyAgainst<TSource>()
        where TSource : class, ICheckpointSource
    {
        AddSourceType(typeof(TSource));

        return this;
    }

    /// <summary>
    ///     Reports a record left unlinked past the given age as an
    ///     <see cref="AuditChainBreakKind.Unlinked" /> break.
    /// </summary>
    /// <param name="age">The age past which an unlinked record is reported. Must be positive.</param>
    /// <returns>The current builder.</returns>
    public AuditHashChainBuilder UnlinkedAfter(TimeSpan age)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(age, TimeSpan.Zero);

        _options.UnlinkedAfter = age;

        return this;
    }

    /// <summary>
    ///     Reports a last checkpoint older than the given age, with records after it, as a
    ///     <see cref="AuditChainBreakKind.CheckpointStale" /> break.
    /// </summary>
    /// <param name="age">The age past which a last checkpoint is stale. Must be positive.</param>
    /// <returns>The current builder.</returns>
    public AuditHashChainBuilder CheckpointStaleAfter(TimeSpan age)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(age, TimeSpan.Zero);

        _options.CheckpointStaleAfter = age;

        return this;
    }

    /// <summary>
    ///     Publishes an erasure notice carrying no hash whenever segments are erased.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Erasure publishes nothing by default. Turn this on only for products that must prove an erasure happened.
    /// </remarks>
    public AuditHashChainBuilder PublishErasureNotices()
    {
        _options.PublishErasureNotices = true;

        return this;
    }

    /// <summary>
    ///     Records a signer type unless it is already registered.
    /// </summary>
    /// <param name="signerType">The signer type to record.</param>
    private void AddSignerType(Type signerType)
    {
        if (!_options.SignerTypes.Contains(signerType))
        {
            _options.SignerTypes = [.. _options.SignerTypes, signerType];
        }
    }

    /// <summary>
    ///     Records a publisher type unless it is already registered.
    /// </summary>
    /// <param name="publisherType">The publisher type to record.</param>
    private void AddPublisherType(Type publisherType)
    {
        if (!_options.PublisherTypes.Contains(publisherType))
        {
            _options.PublisherTypes = [.. _options.PublisherTypes, publisherType];
        }
    }

    /// <summary>
    ///     Records a checkpoint source type unless it is already registered.
    /// </summary>
    /// <param name="sourceType">The source type to record.</param>
    private void AddSourceType(Type sourceType)
    {
        if (!_options.SourceTypes.Contains(sourceType))
        {
            _options.SourceTypes = [.. _options.SourceTypes, sourceType];
        }
    }
}
