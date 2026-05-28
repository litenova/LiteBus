using System;
using Npgsql;

namespace LiteBus.Outbox.PostgreSql;

/// <summary>
///     Configures the PostgreSQL outbox store module.
/// </summary>
public sealed class PostgreSqlOutboxModuleBuilder
{
    /// <summary>
    ///     Gets the PostgreSQL data source used by the store.
    /// </summary>
    public NpgsqlDataSource? DataSource { get; private set; }

    /// <summary>
    ///     Gets the PostgreSQL store options.
    /// </summary>
    public PostgreSqlOutboxStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Sets the PostgreSQL data source used by the store.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <returns>The current builder.</returns>
    public PostgreSqlOutboxModuleBuilder UseDataSource(NpgsqlDataSource dataSource)
    {
        DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        return this;
    }

    /// <summary>
    ///     Replaces the PostgreSQL store options.
    /// </summary>
    /// <param name="options">The store options.</param>
    /// <returns>The current builder.</returns>
    public PostgreSqlOutboxModuleBuilder UseOptions(PostgreSqlOutboxStoreOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }
}