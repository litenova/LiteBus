using Npgsql;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Configures the PostgreSQL saga storage module.
/// </summary>
public sealed class PostgreSqlSagaModuleBuilder
{
    /// <summary>
    ///     Gets the configured PostgreSQL data source.
    /// </summary>
    internal NpgsqlDataSource? DataSource { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this builder created the data source and owns disposal.
    /// </summary>
    internal bool OwnsDataSource { get; private set; }

    /// <summary>
    ///     Gets the saga store options.
    /// </summary>
    public PostgreSqlSagaStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether schema initialization is registered.
    /// </summary>
    internal bool IsSchemaInitializationEnabled { get; private set; } = true;

    /// <summary>
    ///     Uses an existing PostgreSQL data source.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <returns>The current builder.</returns>
    public PostgreSqlSagaModuleBuilder UseDataSource(NpgsqlDataSource dataSource)
    {
        DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        OwnsDataSource = false;
        return this;
    }

    /// <summary>
    ///     Creates a PostgreSQL data source from a connection string.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The current builder.</returns>
    public PostgreSqlSagaModuleBuilder UseConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        DataSource = NpgsqlDataSource.Create(connectionString);
        OwnsDataSource = true;
        return this;
    }

    /// <summary>
    ///     Replaces the default saga store options.
    /// </summary>
    /// <param name="options">The saga store options.</param>
    /// <returns>The current builder.</returns>
    public PostgreSqlSagaModuleBuilder UseOptions(PostgreSqlSagaStoreOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        return this;
    }

    /// <summary>
    ///     Disables automatic schema initialization on host startup.
    /// </summary>
    /// <returns>The current builder.</returns>
    public PostgreSqlSagaModuleBuilder DisableSchemaInitialization()
    {
        IsSchemaInitializationEnabled = false;
        return this;
    }
}