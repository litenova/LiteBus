using System;
using LiteBus.Auditing.Abstractions;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     Assigns stored audit documents to segments with a delegate and opens segments without a genesis document.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
/// <remarks>
///     An application that segments by a delegate registers the delegate in the container, so the container can
///     build this segmenter where the chain configuration names a segmenter type. A chain that opens a segment
///     with a genesis document implements <see cref="IAuditSegmenter{TDocument}" /> instead.
/// </remarks>
public sealed class MartenAuditFuncSegmenter<TDocument> : IAuditSegmenter<TDocument>
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     Gets the delegate that assigns a document to its segment.
    /// </summary>
    private readonly Func<TDocument, string> _segmentOf;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditFuncSegmenter{TDocument}" /> class.
    /// </summary>
    /// <param name="segmentOf">The delegate that assigns a document to its segment.</param>
    public MartenAuditFuncSegmenter(Func<TDocument, string> segmentOf)
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
