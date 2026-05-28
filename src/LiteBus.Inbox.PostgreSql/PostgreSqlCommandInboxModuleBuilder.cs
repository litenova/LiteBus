using System;
using Npgsql;

namespace LiteBus.Inbox.PostgreSql;

/// <summary>
///     Configures the PostgreSQL command inbox store module.
/// </summary>
public sealed class PostgreSqlCommandInboxModuleBuilder
{
    /// <summary>
    ///     Gets the PostgreSQL data source used by the store.
    /// </summary>
    public NpgsqlDataSource? DataSource { get; private set; }

    /// <summary>
    ///     Gets the PostgreSQL store options.
    /// </summary>
    public PostgreSqlInboxStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Sets the PostgreSQL data source used by the store.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <returns>The current builder.</returns>
    public PostgreSqlCommandInboxModuleBuilder UseDataSource(NpgsqlDataSource dataSource)
    {
        DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        return this;
    }

    /// <summary>
    ///     Replaces the PostgreSQL store options.
    /// </summary>
    /// <param name="options">The store options.</param>
    /// <returns>The current builder.</returns>
    public PostgreSqlCommandInboxModuleBuilder UseOptions(PostgreSqlInboxStoreOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
}