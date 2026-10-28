using System;
using Npgsql;

namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     Captures PostgreSQL idempotency store configuration for schema startup tasks.
/// </summary>
public sealed class PostgreSqlIdempotencyStoreRegistration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlIdempotencyStoreRegistration" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The idempotency store options.</param>
    public PostgreSqlIdempotencyStoreRegistration(NpgsqlDataSource dataSource, PostgreSqlIdempotencyStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(options);
        DataSource = dataSource;
        Options = options;
    }

    /// <summary>
    ///     Gets the PostgreSQL data source.
    /// </summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>
    ///     Gets the idempotency store options.
    /// </summary>
    public PostgreSqlIdempotencyStoreOptions Options { get; }
}
