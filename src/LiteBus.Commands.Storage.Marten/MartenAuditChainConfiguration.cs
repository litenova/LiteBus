using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     The hash chain operations for the configured stored audit document type, without naming the type.
/// </summary>
/// <remarks>
///     Built by <see cref="MartenAuditHashChainBuilder" /> and held by
///     <see cref="MartenAuditTrailOptions" />. The trail, the chain, and the checkpoint loop run through this type,
///     so they never name the stored document type; the typed operations live on the generic
///     <c>MartenAuditChainConfiguration</c> subclass the builder constructs.
/// </remarks>
internal abstract class MartenAuditChainConfiguration
{
    /// <summary>
    ///     Gets the stored audit document type the chain writes.
    /// </summary>
    internal abstract Type DocumentType { get; }

    /// <summary>
    ///     Gets the policy that decides when a segment is due for a checkpoint.
    /// </summary>
    internal abstract ICheckpointPolicy CheckpointPolicy { get; }

    /// <summary>
    ///     Appends one audit record to its segment with the next sequence number and a hash over its predecessor.
    /// </summary>
    /// <param name="store">The application-owned Marten document store.</param>
    /// <param name="record">The record produced at the mediation boundary.</param>
    /// <param name="timeProvider">The clock that stamps checkpoint candidates.</param>
    /// <param name="serviceProvider">Resolves pieces the builder named as types, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The token used to cancel the append.</param>
    /// <returns>A task representing the asynchronous append.</returns>
    internal abstract Task AppendAsync(
        IDocumentStore store,
        AuditRecord record,
        TimeProvider timeProvider,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Verifies one segment and compares it against its latest checkpoint.
    /// </summary>
    /// <param name="store">The application-owned Marten document store.</param>
    /// <param name="segment">The segment to verify.</param>
    /// <param name="serviceProvider">Resolves pieces the builder named as types, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The token used to cancel the verification read.</param>
    /// <returns>Every break found, the head, and the checkpoint comparison.</returns>
    internal abstract Task<AuditChainVerification> VerifyAsync(
        IDocumentStore store,
        string segment,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Cuts, stores, signs, and publishes a checkpoint of a segment now.
    /// </summary>
    /// <param name="store">The application-owned Marten document store.</param>
    /// <param name="segment">The segment to checkpoint.</param>
    /// <param name="kind">Why the checkpoint is cut.</param>
    /// <param name="timeProvider">The clock that stamps the checkpoint.</param>
    /// <param name="serviceProvider">Resolves pieces the builder named as types, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The checkpoint that was cut.</returns>
    internal abstract Task<AuditCheckpoint> CutCheckpointAsync(
        IDocumentStore store,
        string segment,
        AuditCheckpointKind kind,
        TimeProvider timeProvider,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Publishes a final checkpoint for a segment, then deletes the segment documents and closes it to new
    ///     writes.
    /// </summary>
    /// <param name="store">The application-owned Marten document store.</param>
    /// <param name="segment">The segment to expire.</param>
    /// <param name="timeProvider">The clock that stamps the final checkpoint.</param>
    /// <param name="serviceProvider">Resolves pieces the builder named as types, or <see langword="null" />.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The final checkpoint.</returns>
    internal abstract Task<AuditCheckpoint> ExpireSegmentAsync(
        IDocumentStore store,
        string segment,
        TimeProvider timeProvider,
        IServiceProvider? serviceProvider,
        CancellationToken cancellationToken);
}
