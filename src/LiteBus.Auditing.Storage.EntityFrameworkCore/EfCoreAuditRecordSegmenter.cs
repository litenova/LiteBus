using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Globalization;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Assigns an <see cref="EfCoreAuditRecord" /> to the segment for its category and year.
/// </summary>
/// <remarks>
///     The default segmenter for the Entity Framework Core audit store: a record lands in
///     <c>{category}/{year}</c>, and a record without a category lands in the <c>uncategorized</c> segment for
///     its year, so the selector never returns a blank segment. Segments open without a genesis document. The
///     described segment metadata carries the record tenant and the <c>category</c> and <c>year</c> attributes,
///     so listing, expiry, and erasure can select segments without parsing keys.
/// </remarks>
public sealed class EfCoreAuditRecordSegmenter : IAuditSegmenter<EfCoreAuditRecord>
{
    /// <summary>
    ///     The category reported for a record that carries none, so the default segment stays stable.
    /// </summary>
    private const string UncategorizedCategory = "uncategorized";

    /// <inheritdoc />
    public string SegmentOf(EfCoreAuditRecord document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return string.Concat(
            CategoryOf(document),
            "/",
            document.OccurredAt.Year.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public EfCoreAuditRecord? CreateGenesis(string segment, EfCoreAuditRecord first)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(first);

        return null;
    }

    /// <inheritdoc />
    public AuditSegmentInfo DescribeSegment(string segment, EfCoreAuditRecord first)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(first);

        var category = CategoryOf(first);

        return new AuditSegmentInfo(
            segment,
            string.IsNullOrWhiteSpace(first.TenantId) ? null : first.TenantId,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["category"] = category,
                ["year"] = first.OccurredAt.Year.ToString(CultureInfo.InvariantCulture)
            },
            first.OccurredAt);
    }

    /// <summary>
    ///     Returns the category a document segments by, reporting the fallback for a record that carries none.
    /// </summary>
    /// <param name="document">The document about to be appended.</param>
    /// <returns>The category used in the segment key and the segment attributes.</returns>
    private static string CategoryOf(EfCoreAuditRecord document)
    {
        return string.IsNullOrWhiteSpace(document.Category) ? UncategorizedCategory : document.Category;
    }
}
