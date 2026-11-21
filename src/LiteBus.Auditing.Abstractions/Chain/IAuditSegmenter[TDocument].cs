using System;
using System.Collections.Generic;

namespace LiteBus.Auditing.Abstractions;

/// <summary>
///     Decides which chain segment a stored audit document belongs to, and whether a segment opens with a genesis
///     document.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     Segments are independent chains. Segmenting by tenant, retention class and year, for example, lets retention
///     expire whole segments while the remaining segments still verify.
/// </remarks>
public interface IAuditSegmenter<TDocument>
{
    /// <summary>
    ///     Returns the segment key of a document.
    /// </summary>
    /// <param name="document">The document about to be appended.</param>
    /// <returns>A stable, non-empty segment key.</returns>
    string SegmentOf(TDocument document);

    /// <summary>
    ///     Creates the genesis document written before the first document of a new segment.
    /// </summary>
    /// <param name="segment">The segment that is being opened.</param>
    /// <param name="first">The first document appended to the segment.</param>
    /// <returns>The genesis document, or <see langword="null" /> when segments open without one.</returns>
    TDocument? CreateGenesis(string segment, TDocument first);

    /// <summary>
    ///     Describes the identity and queryable metadata stored on a segment's head.
    /// </summary>
    /// <param name="segment">The segment key.</param>
    /// <param name="first">The first document appended to the segment.</param>
    /// <returns>The segment info stored on the head.</returns>
    /// <remarks>
    ///     The default implementation records the key with no tenant and no attributes. A segmenter that partitions
    ///     by tenant, retention class, or period overrides this so listing, expiry, and erasure can select segments
    ///     without parsing keys.
    /// </remarks>
    AuditSegmentInfo DescribeSegment(string segment, TDocument first)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(first);

        return new AuditSegmentInfo(
            segment,
            null,
            new Dictionary<string, string>(StringComparer.Ordinal),
            DateTimeOffset.UtcNow);
    }
}
