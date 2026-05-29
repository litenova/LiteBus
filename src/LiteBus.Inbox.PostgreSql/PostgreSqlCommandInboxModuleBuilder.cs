using System;
using LiteBus.PostgreSql;
using Npgsql;

namespace LiteBus.Inbox.PostgreSql;

/// <summary>
///     Configures the PostgreSQL command inbox store module.
/// </summary>
/// <example>
///     Register with an existing data source:
///     <code>
/// var dataSource = NpgsqlDataSource.Create(connectionString);
///
/// liteBus.AddPostgreSqlCommandInboxStore(postgres =>
/// {
///     postgres.UseDataSource(dataSource);
///     postgres.UseOptions(new PostgreSqlInboxStoreOptions { SchemaName = "app" });
/// });
///     </code>
///     Register with a connection string when the module should own the created data source:
///     <code>
/// liteBus.AddPostgreSqlCommandInboxStore(postgres =>
/// {
///     postgres.UseConnectionString(configuration.GetConnectionString("OrdersDb")!);
///     postgres.EnsureSchemaCreationOnStartup();
/// });
///     </code>
/// </example>
public sealed class PostgreSqlCommandInboxModuleBuilder
{
    /// <summary>
    ///     Gets the PostgreSQL data source used by the store.
    /// </summary>
    public NpgsqlDataSource? DataSource { get; private set; }

    /// <summary>
    ///     Gets a value indicating whether this builder created the data source and the module should register it for
    ///     container disposal.
    /// </summary>
    internal bool OwnsDataSource { get; private set; }

    /// <summary>
    ///     Gets the PostgreSQL store options.
    /// </summary>
    public PostgreSqlInboxStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Sets an existing PostgreSQL data source used by the store.
    /// </summary>
    /// <param name="dataSource">A data source owned by the application.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Use this overload when the application already builds and disposes an <see cref="NpgsqlDataSource" />.
    ///     The inbox module does not dispose data sources supplied through this method.
    /// </remarks>
    public PostgreSqlCommandInboxModuleBuilder UseDataSource(NpgsqlDataSource dataSource)
    {
        DataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
        OwnsDataSource = false;
        return this;
    }

    /// <summary>
    ///     Creates a PostgreSQL data source from a connection string and uses it for the inbox store.
    /// </summary>
    /// <param name="connectionString">
    ///     A PostgreSQL connection string, for example
    ///     <c>Host=localhost;Database=orders;Username=app;Password=secret</c>.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The module registers the created <see cref="NpgsqlDataSource" /> with the dependency injection container so
    ///     it can be disposed on application shutdown. When you already share one data source across multiple stores,
    ///     prefer <see cref="UseDataSource(NpgsqlDataSource)" /> instead.
    /// </remarks>
    public PostgreSqlCommandInboxModuleBuilder UseConnectionString(string connectionString)
    {
        DataSource = PostgreSqlDataSourceFactory.CreateFromConnectionString(connectionString);
        OwnsDataSource = true;
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

    /// <summary>
    ///     Enables automatic inbox schema creation or upgrade when the generic host starts.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Requires <see cref="Extensions.Microsoft.Hosting.ModuleRegistryHostingExtensions.AddPostgreSqlCommandInboxSchemaHosting" />.
    ///     Register schema hosting before inbox processor hosting.
    /// </remarks>
    public PostgreSqlCommandInboxModuleBuilder EnsureSchemaCreationOnStartup()
    {
        Options = Options with { EnsureSchemaCreationOnStartup = true };
        return this;
    }
}
