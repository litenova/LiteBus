using System.Threading;
using System.Threading.Tasks;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Creates an outbox database context whose lifetime is limited to one store operation.
/// </summary>
internal interface IEfCoreOutboxDbContextFactory
{
    /// <summary>
    ///     Creates a database context for one outbox store operation.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels context creation.</param>
    /// <returns>The newly created outbox database context.</returns>
    ValueTask<IOutboxDbContext> CreateDbContextAsync(CancellationToken cancellationToken);
}
