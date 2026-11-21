using System;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.Segments;

/// <summary>
///     Assigns stored audit documents to segments with a delegate and opens segments without a genesis document.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     Created by <see cref="AuditHashChainBuilder.SegmentBy{TDocument}(Func{TDocument, string})" /> for any audit
///     store. A chain that opens a segment with a genesis document, or describes a segment's tenant and attributes,
///     implements <see cref="IAuditSegmenter{TDocument}" /> instead.
/// </remarks>
public sealed class AuditFuncSegmenter<TDocument> : IAuditSegmenter<TDocument>
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Gets the delegate that assigns a document to its segment.
    /// </summary>
    private readonly Func<TDocument, string> _segmentOf;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditFuncSegmenter{TDocument}" /> class.
    /// </summary>
    /// <param name="segmentOf">The delegate that assigns a document to its segment.</param>
    public AuditFuncSegmenter(Func<TDocument, string> segmentOf)
    {
        ArgumentNullException.ThrowIfNull(segmentOf);
        _segmentOf = segmentOf;
    }

    /// <inheritdoc />
    public string SegmentOf(TDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return _segmentOf(document);
    }

    /// <inheritdoc />
    public TDocument? CreateGenesis(string segment, TDocument first)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);
        ArgumentNullException.ThrowIfNull(first);

        return null;
    }
}
