using System;
using Npgsql;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Holds the PostgreSQL data source and options registered for the command inbox store.
/// </summary>
/// <remarks>
///     This registration is consumed by optional schema bootstrap hosting and is registered automatically by
///     <see cref="PostgreSqlInboxModule" />.
/// </remarks>
public sealed class PostgreSqlInboxStoreRegistration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxStoreRegistration" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The inbox store options.</param>
    public PostgreSqlInboxStoreRegistration(NpgsqlDataSource dataSource, PostgreSqlInboxStoreOptions options)
    {
        DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    ///     Gets the PostgreSQL data source used by the inbox store.
    /// </summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>
    ///     Gets the inbox store options.
    /// </summary>
    public PostgreSqlInboxStoreOptions Options { get; }
}
