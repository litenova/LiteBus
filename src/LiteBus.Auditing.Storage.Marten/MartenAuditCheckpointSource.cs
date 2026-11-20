using System;
using LiteBus.Auditing.Abstractions;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Marten;

namespace LiteBus.Auditing.Storage.Marten;

/// <summary>
///     Reads the latest stored audit checkpoint of a segment from Marten.
/// </summary>
/// <remarks>
///     The checkpoint source for the Marten audit store: verification compares the chain against what this source
///     returns, so a segment proves coverage without an external store. The chain store itself answers the same
///     read; this type serves applications that want the source without the store.
/// </remarks>
public sealed class MartenAuditCheckpointSource : ICheckpointSource
{
    /// <summary>
    ///     Gets the document store that opens the sessions checkpoint reads run on.
    /// </summary>
    private readonly IDocumentStore _documentStore;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditCheckpointSource" /> class.
    /// </summary>
    /// <param name="documentStore">The application-owned Marten document store.</param>
    public MartenAuditCheckpointSource(IDocumentStore documentStore)
    {
        ArgumentNullException.ThrowIfNull(documentStore);
        _documentStore = documentStore;
    }

    /// <inheritdoc />
    public async ValueTask<AuditCheckpoint?> GetLatestAsync(string segment, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segment);

        var session = _documentStore.QuerySession();

        await using (session.ConfigureAwait(false))
        {
            var checkpoints = await session
                .Query<MartenAuditCheckpoint>()
                .Where(checkpoint => checkpoint.Segment == segment)
                .OrderByDescending(checkpoint => checkpoint.Sequence)
                .ThenByDescending(checkpoint => checkpoint.CreatedAt)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            return checkpoints.Count == 0 ? null : checkpoints[0].ToAuditCheckpoint();
        }
    }
}
