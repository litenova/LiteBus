using Npgsql;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Captures PostgreSQL saga store configuration for schema startup tasks.
/// </summary>
public sealed class PostgreSqlSagaStoreRegistration
{
    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSagaStoreRegistration" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The saga store options.</param>
    public PostgreSqlSagaStoreRegistration(NpgsqlDataSource dataSource, PostgreSqlSagaStoreOptions options)
    {
        DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    /// <summary>
    ///     Gets the PostgreSQL data source.
    /// </summary>
    public NpgsqlDataSource DataSource { get; }

    /// <summary>
    ///     Gets the saga store options.
    /// </summary>
    public PostgreSqlSagaStoreOptions Options { get; }
}