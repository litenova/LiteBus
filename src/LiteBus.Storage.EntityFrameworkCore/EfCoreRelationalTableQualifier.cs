using LiteBus.Storage.EntityFrameworkCore.Exceptions;

namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Builds provider-specific quoted table names for raw SQL leasing commands.
/// </summary>
public static class EfCoreRelationalTableQualifier
{
    /// <summary>
    ///     Builds a quoted schema-qualified table name for the supplied provider.
    /// </summary>
    /// <param name="provider">The storage provider.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The quoted qualified table name.</returns>
    public static string Qualify(EfCoreStorageProvider provider, string schemaName, string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        return provider switch
        {
            EfCoreStorageProvider.PostgreSql => $"\"{schemaName}\".\"{tableName}\"",
            EfCoreStorageProvider.SqlServer  => $"[{schemaName}].[{tableName}]",
            EfCoreStorageProvider.MySql      => $"`{schemaName}`.`{tableName}`",
            EfCoreStorageProvider.InMemory   => $"\"{schemaName}\".\"{tableName}\"",
            EfCoreStorageProvider.Sqlite     => $"\"{schemaName}\".\"{tableName}\"",
            _                                => throw new EfCoreStorageNotSupportedException($"Table qualification is not supported for provider '{provider}'.")
        };
    }
}