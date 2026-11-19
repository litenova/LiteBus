using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;
using Marten;

namespace LiteBus.Commands.Storage.Marten;

/// <summary>
///     Reads the latest stored audit checkpoint of a segment from Marten.
/// </summary>
/// <remarks>
///     The default <see cref="ICheckpointSource" /> for the Marten audit chain: verification compares the chain
///     against what this source returns, so a segment proves coverage without an external store.
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
