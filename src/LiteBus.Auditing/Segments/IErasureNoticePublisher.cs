using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Auditing.Segments;

/// <summary>
///     Publishes audit chain erasure notices to a store the application owns.
/// </summary>
/// <remarks>
///     <para>
///         Erasure publishes nothing by default. When the chain turns erasure notices on, every registered publisher
///         receives one notice per erased segment. The application registers its publishers in its own container; the
///         segment manager resolves every registered publisher when it erases.
///     </para>
///     <para>
///         A publisher that throws fails the erasure it was called from, so a lost notice surfaces instead of passing
///         silently.
///     </para>
/// </remarks>
public interface IErasureNoticePublisher
{
    /// <summary>
    ///     Publishes one audit chain erasure notice.
    /// </summary>
    /// <param name="notice">The notice describing the erased segment. It carries no hash.</param>
    /// <param name="cancellationToken">The token used to cancel the publish.</param>
    /// <returns>A task representing the asynchronous publish.</returns>
    ValueTask PublishAsync(AuditErasureNotice notice, CancellationToken cancellationToken = default);
}
