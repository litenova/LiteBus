using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.EntityFrameworkCore.Stores;

/// <summary>
///     Shared Entity Framework Core bulk and tracked operations for durable inbox and outbox stores.
/// </summary>
internal static class EfCoreDurableStoreOperations
{
    /// <summary>
    ///     Deletes rows matched by a filtered query, using bulk delete when the provider supports it.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="dbContext">The database context executing the delete.</param>
    /// <param name="query">The filtered entity query.</param>
    /// <param name="cancellationToken">A token that cancels the delete operation.</param>
    /// <returns>The number of deleted rows.</returns>
    internal static async Task<int> DeleteMatchingAsync<TEntity>(
        DbContext dbContext,
        IQueryable<TEntity> query,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (dbContext.Database.ProviderName?.Contains("InMemory", StringComparison.Ordinal) == true)
        {
            var matches = await query.ToListAsync(cancellationToken).ConfigureAwait(false);

            if (matches.Count == 0)
            {
                return 0;
            }

            dbContext.Set<TEntity>().RemoveRange(matches);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return matches.Count;
        }

        return await query.ExecuteDeleteAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    ///     Executes one bulk update when supported; otherwise loads one tracked row and saves changes.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    /// <param name="dbContext">The database context executing the update.</param>
    /// <param name="executeUpdate">The bulk update delegate used when the provider supports it.</param>
    /// <param name="trackedQuery">The query that selects the row to update through change tracking.</param>
    /// <param name="applyTracked">Applies the update to the tracked entity.</param>
    /// <param name="cancellationToken">A token that cancels the update.</param>
    /// <returns><see langword="true" /> when a row was updated; otherwise <see langword="false" />.</returns>
    internal static async Task<bool> ExecuteUpdateOrTrackAsync<TEntity>(
        DbContext dbContext,
        Func<Task<int>> executeUpdate,
        IQueryable<TEntity> trackedQuery,
        Action<TEntity> applyTracked,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        if (EfCoreBulkUpdateCapabilities.SupportsExecuteUpdate(dbContext))
        {
            return await executeUpdate().ConfigureAwait(false) > 0;
        }

        var entity = await trackedQuery.SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        applyTracked(entity);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
