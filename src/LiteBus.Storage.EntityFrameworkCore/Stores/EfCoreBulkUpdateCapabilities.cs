using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.EntityFrameworkCore.Stores;

/// <summary>
///     Detects whether the active Entity Framework Core provider supports relational bulk update APIs.
/// </summary>
internal static class EfCoreBulkUpdateCapabilities
{
    /// <summary>
    ///     Gets a value indicating whether <see cref="EntityFrameworkQueryableExtensions.ExecuteUpdateAsync{TSource}" /> is
    ///     supported.
    /// </summary>
    /// <param name="dbContext">The database context executing the update.</param>
    /// <returns><see langword="true" /> when bulk updates are supported; otherwise, <see langword="false" />.</returns>
    /// <exception cref="LiteBus.Storage.EntityFrameworkCore.Exceptions.EfCoreStorageNotSupportedException">
    ///     Thrown when the active provider name is not recognized.
    /// </exception>
    internal static bool SupportsExecuteUpdate(DbContext dbContext)
    {
        var provider = EfCoreProviderResolver.ResolveProviderName(dbContext.Database.ProviderName);
        return provider is not EfCoreStorageProvider.InMemory and not EfCoreStorageProvider.Sqlite;
    }
}
