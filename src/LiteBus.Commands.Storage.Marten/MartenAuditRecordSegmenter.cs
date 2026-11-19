using System;
using System.Globalization;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Assigns a <see cref="MartenAuditRecord" /> to the segment for its category and year.
/// </summary>
/// <remarks>
///     The default segmenter for <see cref="MartenAuditHashChainBuilder" />: a record lands in
///     <c>{category}/{year}</c>, and a record without a category lands in the <c>uncategorized</c> segment for its
///     year, so the selector never returns a blank segment. Segments open without a genesis document.
/// </remarks>
public sealed class MartenAuditRecordSegmenter : IAuditSegmenter<MartenAuditRecord>
{
    /// <summary>
    ///     The category reported for a record that carries none, so the default segment stays stable.
    /// </summary>
    private const string UncategorizedCategory = "uncategorized";

    /// <inheritdoc />
    public string SegmentOf(MartenAuditRecord document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var category = string.IsNullOrWhiteSpace(document.Category) ? UncategorizedCategory : document.Category;

        return string.Concat(category, "/", document.OccurredAt.Year.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public MartenAuditRecord? CreateGenesis(string segment, MartenAuditRecord first)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(first);

        return null;
    }
}
