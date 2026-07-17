using LiteBus.Storage.EntityFrameworkCore.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Resolves the active Entity Framework Core storage provider for leasing and model configuration.
/// </summary>
public static class EfCoreProviderResolver
{
    /// <summary>
    ///     Resolves the storage provider from an explicit override or the active database context.
    /// </summary>
    /// <param name="dbContext">The database context.</param>
    /// <param name="leaseProviderOverride">
    ///     An optional provider override from store options; when set, inference from the context is skipped.
    /// </param>
    /// <returns>The resolved storage provider.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="dbContext" /> is null.</exception>
    /// <exception cref="NotSupportedException">
    ///     Thrown when the active provider is not recognized and no override was supplied.
    /// </exception>
    public static EfCoreStorageProvider Resolve(DbContext dbContext, EfCoreStorageProvider? leaseProviderOverride)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        if (leaseProviderOverride is not null)
        {
            return leaseProviderOverride.Value;
        }

        return ResolveProviderName(dbContext.Database.ProviderName);
    }

    /// <summary>
    ///     Maps a provider name to a storage provider enum value.
    /// </summary>
    /// <param name="providerName">The provider name reported by Entity Framework Core.</param>
    /// <returns>The mapped storage provider.</returns>
    /// <exception cref="Exceptions.EfCoreStorageNotSupportedException">Thrown when the provider name is not recognized.</exception>
    public static EfCoreStorageProvider ResolveProviderName(string? providerName)
    {
        return providerName switch
        {
            EfCoreRelationalProviderNames.InMemory   => EfCoreStorageProvider.InMemory,
            EfCoreRelationalProviderNames.PostgreSql => EfCoreStorageProvider.PostgreSql,
            EfCoreRelationalProviderNames.SqlServer  => EfCoreStorageProvider.SqlServer,
            EfCoreRelationalProviderNames.MySql      => EfCoreStorageProvider.MySql,
            EfCoreRelationalProviderNames.Sqlite     => EfCoreStorageProvider.Sqlite,
            _ => throw new EfCoreStorageNotSupportedException(
                $"Entity Framework provider '{providerName ?? "unknown"}' is not supported for LiteBus relational storage. " +
                "Use PostgreSQL, SQL Server, MySQL (Pomelo), SQLite, or the in-memory provider, or set an explicit lease provider override in store options.")
        };
    }

    /// <summary>
    ///     Gets the default schema name recommended for one storage provider.
    /// </summary>
    /// <param name="provider">The storage provider.</param>
    /// <returns>The recommended default schema name.</returns>
    public static string GetRecommendedDefaultSchema(EfCoreStorageProvider provider)
    {
        return provider switch
        {
            EfCoreStorageProvider.SqlServer  => "dbo",
            EfCoreStorageProvider.PostgreSql => "public",
            EfCoreStorageProvider.MySql      => string.Empty,
            EfCoreStorageProvider.Sqlite     => string.Empty,
            _                                => "dbo"
        };
    }
}
