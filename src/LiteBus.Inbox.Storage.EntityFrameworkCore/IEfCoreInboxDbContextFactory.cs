using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Creates an inbox database context whose lifetime is limited to one store operation.
/// </summary>
internal interface IEfCoreInboxDbContextFactory
{
    /// <summary>
    ///     Creates a database context for one inbox store operation.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels context creation.</param>
    /// <returns>The newly created inbox database context.</returns>
    ValueTask<IInboxDbContext> CreateDbContextAsync(CancellationToken cancellationToken);
}
