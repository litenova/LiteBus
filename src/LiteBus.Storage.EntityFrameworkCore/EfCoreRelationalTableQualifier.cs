using LiteBus.Storage.EntityFrameworkCore.Exceptions;

namespace LiteBus.Storage.EntityFrameworkCore;

/// <summary>
///     Builds provider-specific quoted table names for raw SQL leasing commands.
/// </summary>
public static class EfCoreRelationalTableQualifier
{
    /// <summary>
    ///     Builds a quoted table name, including a schema when the provider supports schemas.
    /// </summary>
    /// <param name="provider">The storage provider.</param>
    /// <param name="schemaName">The schema name.</param>
    /// <param name="tableName">The table name.</param>
    /// <returns>The quoted qualified table name.</returns>
    public static string Qualify(EfCoreStorageProvider provider, string schemaName, string tableName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tableName);

        if (provider is not EfCoreStorageProvider.MySql and not EfCoreStorageProvider.Sqlite)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(schemaName);
        }

        return provider switch
        {
            EfCoreStorageProvider.PostgreSql => $"{QuotePostgreSql(schemaName)}.{QuotePostgreSql(tableName)}",
            EfCoreStorageProvider.SqlServer => $"{QuoteSqlServer(schemaName)}.{QuoteSqlServer(tableName)}",
            EfCoreStorageProvider.MySql => QuoteMySql(tableName),
            EfCoreStorageProvider.InMemory => $"{QuotePostgreSql(schemaName)}.{QuotePostgreSql(tableName)}",
            EfCoreStorageProvider.Sqlite => QuoteSqlite(tableName),
            _ => throw new EfCoreStorageNotSupportedException($"Table qualification is not supported for provider '{provider}'.")
        };
    }

    /// <summary>
    ///     Quotes one PostgreSQL identifier and escapes embedded quote characters.
    /// </summary>
    /// <param name="identifier">The unquoted identifier.</param>
    /// <returns>The quoted identifier.</returns>
    private static string QuotePostgreSql(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (identifier.Contains('\0'))
        {
            throw new ArgumentException("PostgreSQL identifiers cannot contain null characters.", nameof(identifier));
        }

        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>
    ///     Quotes one SQL Server identifier and escapes embedded closing brackets.
    /// </summary>
    /// <param name="identifier">The unquoted identifier.</param>
    /// <returns>The quoted identifier.</returns>
    private static string QuoteSqlServer(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (identifier.Contains('\0'))
        {
            throw new ArgumentException("SQL Server identifiers cannot contain null characters.", nameof(identifier));
        }

        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    /// <summary>
    ///     Quotes one MySQL identifier and escapes embedded backticks.
    /// </summary>
    /// <param name="identifier">The unquoted identifier.</param>
    /// <returns>The quoted identifier.</returns>
    private static string QuoteMySql(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (identifier.Contains('\0'))
        {
            throw new ArgumentException("MySQL identifiers cannot contain null characters.", nameof(identifier));
        }

        return $"`{identifier.Replace("`", "``", StringComparison.Ordinal)}`";
    }

    /// <summary>
    ///     Quotes one SQLite identifier and escapes embedded quote characters.
    /// </summary>
    /// <param name="identifier">The unquoted identifier.</param>
    /// <returns>The quoted identifier.</returns>
    private static string QuoteSqlite(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        if (identifier.Contains('\0'))
        {
            throw new ArgumentException("SQLite identifiers cannot contain null characters.", nameof(identifier));
        }

        return $"\"{identifier.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
