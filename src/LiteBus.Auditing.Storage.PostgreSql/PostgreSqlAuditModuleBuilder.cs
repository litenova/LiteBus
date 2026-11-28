using System;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Configures the PostgreSQL audit store module.
/// </summary>
/// <remarks>
///     Reached through <c>AuditingModuleBuilder.UsePostgreSqlStore</c>: the application passes a configuration
///     action with the store choice, and the action runs when the module builds. That extension records the
///     store types and registers <see cref="PostgreSqlAuditModule" /> as a child of the auditing module; the
///     data source and the table names live here.
/// </remarks>
/// <example>
///     <code><![CDATA[
/// registry.AddAuditing(auditing => auditing
///     .UsePostgreSqlStore(postgres => postgres
///         .UseConnectionString(configuration.GetConnectionString("OrdersDb")!))
///     .UseHashChain(chain => chain
///         .CheckpointStaleAfter(TimeSpan.FromDays(1))));
/// ]]></code>
/// </example>
public sealed class PostgreSqlAuditModuleBuilder
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
    ///     Gets the audit store options.
    /// </summary>
    public PostgreSqlAuditStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether schema initialization is registered.
    /// </summary>
    internal bool IsSchemaInitializationEnabled { get; private set; } = true;

    /// <summary>
    ///     Uses an existing PostgreSQL data source.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="dataSource" /> is <see langword="null" />.</exception>
    /// <remarks>
    ///     Use this overload when the application already builds and disposes an <see cref="NpgsqlDataSource" />.
    ///     The audit module does not dispose data sources supplied through this method. When several LiteBus
    ///     PostgreSQL stores share one database, create one data source and pass it to every store with this
    ///     method.
    /// </remarks>
    public PostgreSqlAuditModuleBuilder UseDataSource(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DataSource = dataSource;
        OwnsDataSource = false;
        return this;
    }

    /// <summary>
    ///     Creates a PostgreSQL data source from a connection string.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentException"><paramref name="connectionString" /> is null, empty, or whitespace.</exception>
    /// <remarks>
    ///     The module registers the created <see cref="NpgsqlDataSource" /> with the dependency injection container
    ///     so it is disposed on application shutdown.
    /// </remarks>
    public PostgreSqlAuditModuleBuilder UseConnectionString(string connectionString)
    {
        DataSource = PostgreSqlDataSourceFactory.CreateFromConnectionString(connectionString);
        OwnsDataSource = true;
        return this;
    }

    /// <summary>
    ///     Replaces the default audit store options.
    /// </summary>
    /// <param name="options">The audit store options.</param>
    /// <returns>The current builder.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options" /> is <see langword="null" />.</exception>
    public PostgreSqlAuditModuleBuilder UseOptions(PostgreSqlAuditStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        return this;
    }

    /// <summary>
    ///     Disables automatic schema initialization on host startup.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Use this when a migration pipeline owns the audit tables. The tables must still exist before the first
    ///     audit record is written, or the write fails.
    /// </remarks>
    public PostgreSqlAuditModuleBuilder DisableSchemaInitialization()
    {
        IsSchemaInitializationEnabled = false;
        return this;
    }
}
