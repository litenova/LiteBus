using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Abstractions;

/// <summary>
///     Participates in inbox envelope dispatch by running logic before and after
///     <see cref="IInboxDispatcher.DispatchAsync" />.
/// </summary>
public interface IInboxProcessorEnvelopeHook
{
    /// <summary>
    ///     Runs before the dispatcher executes one leased envelope.
    /// </summary>
    /// <param name="envelope">The leased inbox envelope.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes before dispatch begins.</returns>
    Task BeforeDispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Runs after dispatch completes successfully for one leased envelope.
    /// </summary>
    /// <param name="envelope">The leased inbox envelope.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that completes after dispatch post-processing finishes.</returns>
    Task AfterDispatchAsync(InboxEnvelope envelope, CancellationToken cancellationToken = default);
}
