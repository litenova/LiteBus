using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Engine;

/// <summary>
///     Operates on the hash-chained audit trail by delegating to the chain engine components.
/// </summary>
/// <remarks>
///     <para>
///         The store-neutral <see cref="IAuditChain" />: verification runs on <see cref="IAuditVerifier" />,
///         checkpoints on <see cref="ICheckpointLifecycle" />, and segments on
///         <see cref="IAuditSegmentManager" />. Checkpoints are read back through
///         <see cref="IAuditCheckpointStore" />, so trusting an anchored checkpoint and listing unpublished ones
///         need no document type.
///     </para>
///     <para>
///         One instance serves every caller: the components it delegates to hold no per-call state.
///     </para>
/// </remarks>
public sealed class AuditChain : IAuditChain
{
    /// <summary>
    ///     Verifies one segment against its checkpoints. Held for the life of the chain.
    /// </summary>
    private readonly IAuditVerifier _verifier;

    /// <summary>
    ///     Cuts checkpoints and publishes pending ones. Held for the life of the chain.
    /// </summary>
    private readonly ICheckpointLifecycle _checkpoints;

    /// <summary>
    ///     Lists, expires, and erases segments. Held for the life of the chain.
    /// </summary>
    private readonly IAuditSegmentManager _segments;

    /// <summary>
    ///     Reads stored checkpoints back by identity. Held for the life of the chain.
    /// </summary>
    private readonly IAuditCheckpointStore _checkpointStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditChain" /> class.
    /// </summary>
    /// <param name="verifier">The verifier one segment is verified with.</param>
    /// <param name="checkpoints">The lifecycle checkpoints are cut with.</param>
    /// <param name="segments">The manager segments are listed, expired, and erased with.</param>
    /// <param name="checkpointStore">The store checkpoints are read back from.</param>
    internal AuditChain(
        IAuditVerifier verifier,
        ICheckpointLifecycle checkpoints,
        IAuditSegmentManager segments,
        IAuditCheckpointStore checkpointStore)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        ArgumentNullException.ThrowIfNull(checkpoints);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(checkpointStore);

        _verifier = verifier;
        _checkpoints = checkpoints;
        _segments = segments;
        _checkpointStore = checkpointStore;
    }

    /// <inheritdoc />
    public async Task<AuditChainVerification> VerifyAsync(string segment, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        return await VerifyAsync(new AuditChainVerificationRequest(segment), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuditChainVerification> VerifyAsync(
        AuditChainVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Segment);

        return await _verifier.VerifyAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuditChainVerification> VerifyFromAsync(
        string segment,
        Guid checkpointId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        if (checkpointId == Guid.Empty)
        {
            throw new ArgumentException("The checkpoint identifier must not be empty.", nameof(checkpointId));
        }

        var checkpoint = await _checkpointStore.ReadCheckpointAsync(checkpointId, cancellationToken).ConfigureAwait(false);

        if (checkpoint is null)
        {
            return UntrustedCheckpointVerification(
                segment,
                $"No stored checkpoint with the identifier '{checkpointId}' exists, so verification of segment " +
                $"'{segment}' has nothing to trust.");
        }

        if (!string.Equals(checkpoint.Segment, segment, StringComparison.Ordinal))
        {
            return UntrustedCheckpointVerification(
                segment,
                $"Checkpoint '{checkpointId}' belongs to segment '{checkpoint.Segment}', so it cannot anchor " +
                $"verification of segment '{segment}'.");
        }

        return await _verifier
            .VerifyAsync(new AuditChainVerificationRequest(segment, checkpoint.Sequence), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AuditSegmentSummary> ListSegmentsAsync(
        AuditSegmentFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        return _segments.ListAsync(filter, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AuditCheckpoint> ListUnpublishedCheckpointsAsync(
        CancellationToken cancellationToken = default)
    {
        return _checkpointStore.ListUnpublishedAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<int> EraseAsync(
        AuditSegmentFilter filter,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return await _segments.EraseAsync(filter, reason, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuditCheckpoint> CutCheckpointAsync(string segment, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        return await _checkpoints
            .CutAsync(segment, null, AuditCheckpointKind.Regular, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuditCheckpoint> CutCheckpointAsync(
        string segment,
        string label,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        return await _checkpoints
            .CutAsync(segment, label, AuditCheckpointKind.Regular, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuditCheckpoint> ExpireSegmentAsync(string segment, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        return await _segments.ExpireAsync(segment, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Builds the verification result for a checkpoint that cannot anchor a segment.
    /// </summary>
    /// <param name="segment">The segment verification was asked for.</param>
    /// <param name="detail">A sentence describing why the checkpoint cannot anchor it, for an operator.</param>
    /// <returns>The verification carrying one missing-checkpoint break and no records checked.</returns>
    private static AuditChainVerification UntrustedCheckpointVerification(string segment, string detail)
    {
        return new AuditChainVerification
        {
            Segment = segment,
            RecordCount = 0,
            Breaks = [new AuditChainBreak(0, AuditChainBreakKind.CheckpointMissing, detail)],
        };
    }
}
