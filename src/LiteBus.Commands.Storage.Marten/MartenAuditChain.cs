using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Verifies, checkpoints, and expires Marten hash-chained audit segments.
/// </summary>
/// <remarks>
///     <para>
///         The operational side of the chained <see cref="MartenAuditTrail" />: verification recomputes every hash
///         with the scheme each record names and compares the head against the latest stored checkpoint, an explicit
///         cut anchors a segment now, and expiry publishes a final checkpoint before retention deletes the segment.
///         Expiry stores the final checkpoint first, then publishes it, and only then deletes the segment documents
///         and marks the head expired; a stored final checkpoint is reused when expiry retries, so the published
///         checkpoint keeps its identity. Each segment is an independent chain, so expiring one never breaks the
///         verification of the rest.
///     </para>
///     <para>
///         The chain opens a short session per call from <see cref="IDocumentStore" />, so one instance serves every
///         caller. The application registers Marten itself with <c>AddMarten</c>; LiteBus does not register Marten.
///     </para>
/// </remarks>
public sealed class MartenAuditChain : IAuditChain
{
    /// <summary>
    ///     Gets the document store that opens the sessions chain operations run on.
    /// </summary>
    private readonly IDocumentStore _documentStore;

    /// <summary>
    ///     Gets the hash chain, segmenting, and checkpoint configuration.
    /// </summary>
    private readonly MartenAuditTrailOptions _options;

    /// <summary>
    ///     Gets the clock that stamps cut checkpoints.
    /// </summary>
    private readonly TimeProvider _timeProvider;

    /// <summary>
    ///     Gets the provider that resolves pieces the builder named as types, or <see langword="null" />.
    /// </summary>
    private readonly IServiceProvider? _serviceProvider;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditChain" /> class.
    /// </summary>
    /// <param name="documentStore">The application-owned Marten document store.</param>
    /// <param name="options">
    ///     The hash chain configuration, or <see langword="null" /> for an unconfigured chain that rejects every
    ///     operation until <see cref="MartenAuditTrailOptions.UseHashChain" /> is called.
    /// </param>
    /// <param name="timeProvider">
    ///     The clock that stamps cut checkpoints, or <see langword="null" /> for
    ///     <see cref="TimeProvider.System" />.
    /// </param>
    /// <param name="serviceProvider">
    ///     Resolves pieces the builder named as types, or <see langword="null" /> when every piece was given as an
    ///     instance.
    /// </param>
    public MartenAuditChain(
        IDocumentStore documentStore,
        MartenAuditTrailOptions? options = null,
        TimeProvider? timeProvider = null,
        IServiceProvider? serviceProvider = null)
    {
        ArgumentNullException.ThrowIfNull(documentStore);

        options ??= new MartenAuditTrailOptions();
        options.Validate();

        _documentStore = documentStore;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public Task<AuditChainVerification> VerifyAsync(string segment, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        return _options.RequireChain().VerifyAsync(_documentStore, segment, _serviceProvider, cancellationToken);
    }

    /// <inheritdoc />
    public Task<AuditCheckpoint> CutCheckpointAsync(string segment, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        return _options.RequireChain().CutCheckpointAsync(
            _documentStore,
            segment,
            AuditCheckpointKind.Regular,
            _timeProvider,
            _serviceProvider,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<AuditCheckpoint> ExpireSegmentAsync(string segment, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        return _options.RequireChain().ExpireSegmentAsync(
            _documentStore,
            segment,
            _timeProvider,
            _serviceProvider,
            cancellationToken);
    }
}
