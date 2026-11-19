using System;
using LiteBus.Auditing.Abstractions;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions.Auditing;

namespace LiteBus.Auditing.Engine;

/// <summary>
///     Reads stored audit checkpoints back through the configured chain store.
/// </summary>
/// <typeparam name="TDocument">The stored audit document type.</typeparam>
internal sealed class AuditCheckpointStore<TDocument> : IAuditCheckpointStore
    where TDocument : class, IAuditChainDocument
{
    /// <summary>
    ///     The durable chain operations checkpoints are read from. Held for the life of the adapter.
    /// </summary>
    private readonly IAuditChainStore<TDocument> _store;

    /// <summary>
    ///     Initializes a new instance of the <see cref="AuditCheckpointStore{TDocument}" /> class.
    /// </summary>
    /// <param name="store">The durable chain operations checkpoints are read from.</param>
    internal AuditCheckpointStore(IAuditChainStore<TDocument> store)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
    }

    /// <inheritdoc />
    public ValueTask<AuditCheckpoint?> ReadCheckpointAsync(Guid checkpointId, CancellationToken cancellationToken)
    {
        return _store.ReadCheckpointAsync(checkpointId, cancellationToken);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<AuditCheckpoint> ListUnpublishedAsync(CancellationToken cancellationToken)
    {
        return _store.ListUnpublishedAsync(cancellationToken);
    }
}
