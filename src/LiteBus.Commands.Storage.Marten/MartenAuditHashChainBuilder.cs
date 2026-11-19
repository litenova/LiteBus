using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Messaging.Audit;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Configures how the Marten audit trail chains, segments, hashes, checkpoints, signs, and publishes audit
///     records.
/// </summary>
/// <remarks>
///     <para>
///         Reached through <see cref="MartenAuditTrailOptions.UseHashChain" />. Every piece has a LiteBus default, so
///         a chain that needs nothing special writes no code: records are stored as <see cref="MartenAuditRecord" />,
///         segmented by category and year, hashed with <see cref="MartenAuditHashSchemes.Sha256V1" />, and
///         checkpointed every one hundred records with no signer and no external publisher.
///     </para>
///     <para>
///         A piece that may need services can be given either as an instance or as a type resolved from the
///         dependency registry when the chain runs. A hash scheme can also be given as a factory that receives
///         that same provider, so a hasher that needs container services, such as a key from a secret store
///         client, builds its scheme when the chain runs. The checkpoint policy is a pure value, so it is given
///         as an instance only.
///     </para>
///     <para>
///         Customizing the stored document starts with <c>StoreAs</c>: every other typed call must name the same
///         document type.
///     </para>
/// </remarks>
public sealed class MartenAuditHashChainBuilder
{
    /// <summary>
    ///     The stored audit document type the chain writes.
    /// </summary>
    private Type _documentType = typeof(MartenAuditRecord);

    /// <summary>
    ///     A value indicating whether <c>StoreAs</c> named the document type explicitly.
    /// </summary>
    private bool _documentTypeSet;

    /// <summary>
    ///     Builds the typed chain operations for the configured document type.
    /// </summary>
    private Func<MartenAuditChainConfiguration> _buildFactory;

    /// <summary>
    ///     The configured record mapper instance, when one was given.
    /// </summary>
    private object? _mapper;

    /// <summary>
    ///     The configured record mapper type, when one was given instead of an instance.
    /// </summary>
    private Type? _mapperType;

    /// <summary>
    ///     The configured segmenter instance, when one was given.
    /// </summary>
    private object? _segmenter;

    /// <summary>
    ///     The configured segmenter type, when one was given instead of an instance.
    /// </summary>
    private Type? _segmenterType;

    /// <summary>
    ///     The configured write scheme instance or factory, when one was given.
    /// </summary>
    private object? _writeScheme;

    /// <summary>
    ///     The configured accepted scheme instances and factories for verifying older records, in call order.
    /// </summary>
    private readonly List<object> _acceptedSchemes = [];

    /// <summary>
    ///     The configured checkpoint policy, when one was given.
    /// </summary>
    private ICheckpointPolicy? _policy;

    /// <summary>
    ///     The configured checkpoint signer instance, when one was given.
    /// </summary>
    private object? _signer;

    /// <summary>
    ///     The configured checkpoint signer type, when one was given instead of an instance.
    /// </summary>
    private Type? _signerType;

    /// <summary>
    ///     The configured checkpoint publisher instance, when one was given.
    /// </summary>
    private object? _publisher;

    /// <summary>
    ///     The configured checkpoint publisher type, when one was given instead of an instance.
    /// </summary>
    private Type? _publisherType;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditHashChainBuilder" /> class.
    /// </summary>
    internal MartenAuditHashChainBuilder()
    {
        _buildFactory = BuildDefault;
    }

    /// <summary>
    ///     Stores chained records as the given document type, mapped from each audit record.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="mapper">The mapper from an audit record to a new stored document.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The stored document must persist the chain fields with public getters and setters, so Marten can query
    ///     by segment and order by sequence. Call this before any other typed call when the chain stores a document
    ///     type other than <see cref="MartenAuditRecord" />.
    /// </remarks>
    public MartenAuditHashChainBuilder StoreAs<TDocument>(IAuditRecordMapper<TDocument> mapper)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(mapper);
        SetDocumentType(typeof(TDocument));

        _mapper = mapper;
        _mapperType = null;
        _buildFactory = BuildTyped<TDocument>;

        return this;
    }

    /// <summary>
    ///     Stores chained records as the given document type, mapped by a mapper resolved from the dependency
    ///     registry.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <typeparam name="TMapper">The mapper implementation, constructed by the container when the chain runs.</typeparam>
    /// <returns>The current builder.</returns>
    public MartenAuditHashChainBuilder StoreAs<TDocument, TMapper>()
        where TDocument : class, IAuditChainDocument
        where TMapper : class, IAuditRecordMapper<TDocument>
    {
        SetDocumentType(typeof(TDocument));

        _mapper = null;
        _mapperType = typeof(TMapper);
        _buildFactory = BuildTyped<TDocument>;

        return this;
    }

    /// <summary>
    ///     Assigns stored documents to chain segments with the given segmenter.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="segmenter">The segmenter that names a segment per document and opens segments.</param>
    /// <returns>The current builder.</returns>
    public MartenAuditHashChainBuilder SegmentBy<TDocument>(IAuditSegmenter<TDocument> segmenter)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(segmenter);
        ThrowIfDocumentMismatch(typeof(TDocument), nameof(SegmentBy));

        _segmenter = segmenter;
        _segmenterType = null;

        return this;
    }

    /// <summary>
    ///     Assigns stored documents to chain segments with a delegate, opening segments without a genesis document.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="segmentOf">The delegate that assigns a document to its segment.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     A chain that opens a segment with a genesis document passes an
    ///     <see cref="IAuditSegmenter{TDocument}" /> instead.
    /// </remarks>
    public MartenAuditHashChainBuilder SegmentBy<TDocument>(Func<TDocument, string> segmentOf)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(segmentOf);

        return SegmentBy(new MartenAuditFuncSegmenter<TDocument>(segmentOf));
    }

    /// <summary>
    ///     Assigns stored documents to chain segments with a segmenter resolved from the dependency registry.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <typeparam name="TSegmenter">The segmenter implementation, constructed by the container when the chain runs.</typeparam>
    /// <returns>The current builder.</returns>
    public MartenAuditHashChainBuilder SegmentBy<TDocument, TSegmenter>()
        where TDocument : class, IAuditChainDocument
        where TSegmenter : class, IAuditSegmenter<TDocument>
    {
        ThrowIfDocumentMismatch(typeof(TDocument), nameof(SegmentBy));

        _segmenter = null;
        _segmenterType = typeof(TSegmenter);

        return this;
    }

    /// <summary>
    ///     Hashes newly appended records with the given scheme.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="scheme">The scheme new records are written with.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The write scheme is always accepted for verification. A chain that changes its algorithm, key, or
    ///     canonical form keeps accepting the scheme its history was written with through
    ///     <see cref="AcceptScheme{TDocument}(AuditHashScheme{TDocument})" />.
    /// </remarks>
    public MartenAuditHashChainBuilder UseScheme<TDocument>(AuditHashScheme<TDocument> scheme)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ThrowIfDocumentMismatch(typeof(TDocument), nameof(UseScheme));

        _writeScheme = scheme;

        return this;
    }

    /// <summary>
    ///     Hashes newly appended records with the scheme a factory builds from container services.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="factory">The factory that builds the scheme new records are written with.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The factory runs with the same provider the type-named pieces use, when the chain appends or verifies,
    ///     so the scheme can depend on container services such as a secret store client. The write scheme is always
    ///     accepted for verification. A chain that changes its algorithm, key, or canonical form keeps accepting
    ///     the scheme its history was written with through <see cref="AcceptScheme{TDocument}(AuditHashScheme{TDocument})" />.
    /// </remarks>
    public MartenAuditHashChainBuilder UseScheme<TDocument>(Func<IServiceProvider, AuditHashScheme<TDocument>> factory)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(factory);
        ThrowIfDocumentMismatch(typeof(TDocument), nameof(UseScheme));

        _writeScheme = factory;

        return this;
    }

    /// <summary>
    ///     Accepts the given scheme when verifying records written before the current write scheme.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="scheme">An older scheme history may still carry.</param>
    /// <returns>The current builder.</returns>
    public MartenAuditHashChainBuilder AcceptScheme<TDocument>(AuditHashScheme<TDocument> scheme)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(scheme);
        ThrowIfDocumentMismatch(typeof(TDocument), nameof(AcceptScheme));

        _acceptedSchemes.Add(scheme);

        return this;
    }

    /// <summary>
    ///     Accepts the scheme a factory builds from container services when verifying records written before the
    ///     current write scheme.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <param name="factory">The factory that builds an older scheme history may still carry.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The factory runs with the same provider the type-named pieces use, when the chain verifies, so the
    ///     scheme can depend on container services such as a secret store client.
    /// </remarks>
    public MartenAuditHashChainBuilder AcceptScheme<TDocument>(Func<IServiceProvider, AuditHashScheme<TDocument>> factory)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(factory);
        ThrowIfDocumentMismatch(typeof(TDocument), nameof(AcceptScheme));

        _acceptedSchemes.Add(factory);

        return this;
    }

    /// <summary>
    ///     Cuts a checkpoint whenever the given policy says a segment is due.
    /// </summary>
    /// <param name="policy">The policy that decides when a checkpoint is due.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The chain asks the policy after every append. A time-based policy is also asked on an interval by
    ///     <see cref="MartenAuditCheckpointBackgroundService" />, so a quiet segment still gets its checkpoint.
    ///     The policy is a pure value, so it is given as an instance only.
    /// </remarks>
    public MartenAuditHashChainBuilder CheckpointWhen(ICheckpointPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);

        _policy = policy;

        return this;
    }

    /// <summary>
    ///     Signs every cut checkpoint with the given signer.
    /// </summary>
    /// <param name="signer">The signer that signs checkpoints and verifies their signatures.</param>
    /// <returns>The current builder.</returns>
    public MartenAuditHashChainBuilder SignCheckpointsWith(ICheckpointSigner signer)
    {
        ArgumentNullException.ThrowIfNull(signer);

        _signer = signer;
        _signerType = null;

        return this;
    }

    /// <summary>
    ///     Signs every cut checkpoint with a signer resolved from the dependency registry.
    /// </summary>
    /// <typeparam name="TSigner">The signer implementation, constructed by the container when the chain runs.</typeparam>
    /// <returns>The current builder.</returns>
    public MartenAuditHashChainBuilder SignCheckpointsWith<TSigner>()
        where TSigner : class, ICheckpointSigner
    {
        _signer = null;
        _signerType = typeof(TSigner);

        return this;
    }

    /// <summary>
    ///     Publishes every cut checkpoint to the given publisher after storing it.
    /// </summary>
    /// <param name="publisher">The publisher that receives checkpoints.</param>
    /// <returns>The current builder.</returns>
    public MartenAuditHashChainBuilder PublishCheckpointsTo(ICheckpointPublisher publisher)
    {
        ArgumentNullException.ThrowIfNull(publisher);

        _publisher = publisher;
        _publisherType = null;

        return this;
    }

    /// <summary>
    ///     Publishes every cut checkpoint to a publisher resolved from the dependency registry.
    /// </summary>
    /// <typeparam name="TPublisher">The publisher implementation, constructed by the container when the chain runs.</typeparam>
    /// <returns>The current builder.</returns>
    public MartenAuditHashChainBuilder PublishCheckpointsTo<TPublisher>()
        where TPublisher : class, ICheckpointPublisher
    {
        _publisher = null;
        _publisherType = typeof(TPublisher);

        return this;
    }

    /// <summary>
    ///     Builds the chain operations for the configured document type, filling the defaults for pieces the
    ///     configuration left unset.
    /// </summary>
    /// <returns>The built chain operations.</returns>
    /// <exception cref="InvalidOperationException">
    ///     A custom document type misses its mapper, segmenter, or write scheme, or two different schemes share a
    ///     name.
    /// </exception>
    internal MartenAuditChainConfiguration Build()
    {
        return _buildFactory();
    }

    /// <summary>
    ///     Builds the chain operations for the default document type.
    /// </summary>
    /// <returns>The built chain operations.</returns>
    private MartenAuditChainConfiguration<MartenAuditRecord> BuildDefault()
    {
        return BuildTyped<MartenAuditRecord>();
    }

    /// <summary>
    ///     Builds the chain operations for one document type.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <returns>The built chain operations.</returns>
    private MartenAuditChainConfiguration<TDocument> BuildTyped<TDocument>()
        where TDocument : class, IAuditChainDocument
    {
        var mapper = (IAuditRecordMapper<TDocument>?) _mapper;
        var segmenter = (IAuditSegmenter<TDocument>?) _segmenter;
        var writeScheme = _writeScheme as AuditHashScheme<TDocument>;
        var writeSchemeFactory = _writeScheme as Func<IServiceProvider, AuditHashScheme<TDocument>>;

        if (_writeScheme is not null && writeScheme is null && writeSchemeFactory is null)
        {
            throw new InvalidOperationException(
                $"The write scheme was configured for another document type. Call UseScheme with an AuditHashScheme<{typeof(TDocument).Name}> or a factory that builds one.");
        }

        if (mapper is null && _mapperType is null)
        {
            mapper = DefaultMapper<TDocument>();
        }

        if (segmenter is null && _segmenterType is null)
        {
            segmenter = DefaultSegmenter<TDocument>();
        }

        if (writeScheme is null && writeSchemeFactory is null)
        {
            writeScheme = DefaultScheme<TDocument>();
        }

        List<AuditHashScheme<TDocument>> acceptedInstances = [];
        List<Func<IServiceProvider, AuditHashScheme<TDocument>>> acceptedFactories = [];

        foreach (var candidate in _acceptedSchemes)
        {
            if (candidate is AuditHashScheme<TDocument> instance)
            {
                acceptedInstances.Add(instance);
            }
            else if (candidate is Func<IServiceProvider, AuditHashScheme<TDocument>> factory)
            {
                acceptedFactories.Add(factory);
            }
            else
            {
                throw new InvalidOperationException(
                    $"An accepted scheme was configured for another document type. Call AcceptScheme with an AuditHashScheme<{typeof(TDocument).Name}> or a factory that builds one.");
            }
        }

        List<AuditHashScheme<TDocument>> accepted = [];
        IEnumerable<AuditHashScheme<TDocument>> candidates = acceptedInstances;

        if (writeScheme is not null)
        {
            candidates = candidates.Prepend(writeScheme);
        }

        foreach (var group in candidates.GroupBy(static scheme => scheme.Name, StringComparer.Ordinal))
        {
            var distinct = group.Distinct().ToList();

            if (distinct.Count > 1)
            {
                throw new InvalidOperationException(
                    $"Two different hash schemes are named '{group.Key}'. Hash scheme names must be unique within a chain.");
            }

            accepted.Add(distinct[0]);
        }

        return new MartenAuditChainConfiguration<TDocument>(
            mapper,
            _mapperType,
            segmenter,
            _segmenterType,
            writeScheme,
            writeSchemeFactory,
            accepted,
            acceptedFactories,
            _policy ?? CheckpointPolicies.EveryRecords(MartenAuditTrailOptions.DefaultCheckpointInterval),
            (ICheckpointSigner?) _signer,
            _signerType,
            (ICheckpointPublisher?) _publisher,
            _publisherType);
    }

    /// <summary>
    ///     Returns the default record mapper for a document type.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <returns>The default mapper.</returns>
    /// <exception cref="InvalidOperationException">The document type has no default mapper.</exception>
    [SuppressMessage("Performance", "CA1859", Justification = "The method is generic over the document type; the concrete default exists only for MartenAuditRecord.")]
    private static IAuditRecordMapper<TDocument> DefaultMapper<TDocument>()
        where TDocument : class, IAuditChainDocument
    {
        if (typeof(TDocument) == typeof(MartenAuditRecord))
        {
            return (IAuditRecordMapper<TDocument>) (object) new MartenAuditRecordMapper();
        }

        throw new InvalidOperationException(
            $"No audit record mapper is configured for document type '{typeof(TDocument).FullName}'. " +
            $"Call StoreAs<{typeof(TDocument).Name}> with an IAuditRecordMapper<{typeof(TDocument).Name}>.");
    }

    /// <summary>
    ///     Returns the default segmenter for a document type.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <returns>The default segmenter.</returns>
    /// <exception cref="InvalidOperationException">The document type has no default segmenter.</exception>
    [SuppressMessage("Performance", "CA1859", Justification = "The method is generic over the document type; the concrete default exists only for MartenAuditRecord.")]
    private static IAuditSegmenter<TDocument> DefaultSegmenter<TDocument>()
        where TDocument : class, IAuditChainDocument
    {
        if (typeof(TDocument) == typeof(MartenAuditRecord))
        {
            return (IAuditSegmenter<TDocument>) (object) new MartenAuditRecordSegmenter();
        }

        throw new InvalidOperationException(
            $"No audit segmenter is configured for document type '{typeof(TDocument).FullName}'. " +
            $"Call SegmentBy with an IAuditSegmenter<{typeof(TDocument).Name}>.");
    }

    /// <summary>
    ///     Returns the default write scheme for a document type.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type.</typeparam>
    /// <returns>The default write scheme.</returns>
    /// <exception cref="InvalidOperationException">The document type has no default scheme.</exception>
    private static AuditHashScheme<TDocument> DefaultScheme<TDocument>()
        where TDocument : class, IAuditChainDocument
    {
        if (typeof(TDocument) == typeof(MartenAuditRecord))
        {
            return (AuditHashScheme<TDocument>) (object) MartenAuditHashSchemes.Sha256V1;
        }

        throw new InvalidOperationException(
            $"No hash scheme is configured for document type '{typeof(TDocument).FullName}'. " +
            $"Call UseScheme with an AuditHashScheme<{typeof(TDocument).Name}>.");
    }

    /// <summary>
    ///     Names the stored document type, failing when another type was already named.
    /// </summary>
    /// <param name="documentType">The stored audit document type.</param>
    /// <exception cref="InvalidOperationException">Another document type was already named.</exception>
    private void SetDocumentType(Type documentType)
    {
        if (_documentTypeSet && _documentType != documentType)
        {
            throw new InvalidOperationException(
                $"StoreAs was already called for '{_documentType.Name}'. A chain stores one document type; remove the second StoreAs call.");
        }

        _documentType = documentType;
        _documentTypeSet = true;
    }

    /// <summary>
    ///     Fails when a typed call names a document type other than the stored one.
    /// </summary>
    /// <param name="documentType">The document type the call named.</param>
    /// <param name="method">The builder method that was called.</param>
    /// <exception cref="InvalidOperationException">The call named another document type.</exception>
    private void ThrowIfDocumentMismatch(Type documentType, string method)
    {
        if (_documentType != documentType)
        {
            throw new InvalidOperationException(
                $"The chain stores '{_documentType.Name}' but {method} named '{documentType.Name}'. " +
                $"Call StoreAs<{documentType.Name}> first, or pass a piece for '{_documentType.Name}'.");
        }
    }
}
