using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Abstractions;

/// <summary>
///     Persists state changes for a batch of envelopes that have already been transitioned
///     by the caller via <see cref="OutboxEnvelope.AsPublished" />,
///     <see cref="OutboxEnvelope.AsFailed" />, or <see cref="OutboxEnvelope.AsDeadLettered" />.
/// </summary>
/// <remarks>
///     Implementations group the batch by <see cref="OutboxStatus" /> and execute the most efficient write for each group.
/// </remarks>
public interface IOutboxStateWriter
{
    /// <summary>
    ///     Persists post-transition envelopes produced during one processor pass.
    /// </summary>
    /// <param name="envelopes">The envelopes to persist.</param>
    /// <param name="cancellationToken">A token that cancels the persistence operation.</param>
    /// <returns>A task that represents the asynchronous persistence operation.</returns>
    Task PersistAsync(IReadOnlyList<OutboxEnvelope> envelopes, CancellationToken cancellationToken = default);
}
