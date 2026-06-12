using System;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Configures the PostgreSQL inbox store module.
/// </summary>
/// <example>
///     Register with an existing data source:
///     <code>
/// var dataSource = NpgsqlDataSource.Create(connectionString);
/// 
/// liteBus.AddPostgreSqlInboxStorage(postgres =>
/// {
///     postgres.UseDataSource(dataSource);
///     postgres.UseOptions(new PostgreSqlInboxStoreOptions { SchemaName = "app" });
/// });
///     </code>
///     Register with a connection string when the module should own the created data source:
///     <code>
/// liteBus.AddPostgreSqlInboxStorage(postgres =>
/// {
///     postgres.UseConnectionString(configuration.GetConnectionString("OrdersDb")!);
///     postgres.EnsureSchemaCreationOnStartup();
/// });
///     </code>
/// </example>
public sealed class PostgreSqlInboxModuleBuilder
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
    ///     Gets a value indicating whether <see cref="PostgreSqlInboxSchemaInitializer" /> is registered.
    /// </summary>
    public bool EnableSchemaInitialization { get; private set; } = true;

    /// <summary>
    ///     Gets a value indicating whether scoped <see cref="LiteBus.Inbox.Abstractions.ITransactionalInbox" /> is registered
    ///     through the ambient PostgreSQL transaction provider.
    /// </summary>
    public bool EnableAmbientTransactionProviderRegistration { get; private set; }

    /// <summary>
    ///     Gets the transactional write mode used when ambient registration is enabled.
    /// </summary>
    public TransactionalWriteMode TransactionalWriteMode { get; private set; } = TransactionalWriteMode.RequireActiveTransaction;

    /// <summary>
    ///     Disables registration of inbox schema initialization background service work.
    /// </summary>
    /// <returns>The current builder.</returns>
    public PostgreSqlInboxModuleBuilder DisableSchemaInitialization()
    {
        EnableSchemaInitialization = false;
        return this;
    }

    /// <summary>
    ///     Sets an existing PostgreSQL data source used by the store.
    /// </summary>
    /// <param name="dataSource">A data source owned by the application.</param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Use this overload when the application already builds and disposes an <see cref="NpgsqlDataSource" />.
    ///     The inbox module does not dispose data sources supplied through this method.
    /// </remarks>
    public PostgreSqlInboxModuleBuilder UseDataSource(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DataSource = dataSource;
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
    public PostgreSqlInboxModuleBuilder UseConnectionString(string connectionString)
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
    public PostgreSqlInboxModuleBuilder UseOptions(PostgreSqlInboxStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Options = options;
        return this;
    }

    /// <summary>
    ///     Enables automatic inbox schema creation or upgrade when the generic host starts.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Schema bootstrap runs through <see cref="PostgreSqlInboxSchemaInitializer" /> when
    ///     <see cref="EnableSchemaInitialization" /> is enabled.
    /// </remarks>
    public PostgreSqlInboxModuleBuilder EnsureSchemaCreationOnStartup()
    {
        Options = Options with { EnsureSchemaCreationOnStartup = true };
        return this;
    }

    /// <summary>
    ///     Registers scoped <see cref="LiteBus.Inbox.Abstractions.ITransactionalInbox" /> resolved through
    ///     <see cref="IPostgreSqlTransactionProvider" /> when the application supplies one in the current scope.
    /// </summary>
    /// <param name="mode">
    ///     Controls behavior when no ambient transaction is active. Defaults to
    ///     <see cref="TransactionalWriteMode.RequireActiveTransaction" />.
    /// </param>
    /// <returns>The current builder.</returns>
    public PostgreSqlInboxModuleBuilder EnableAmbientTransactionProvider(
        TransactionalWriteMode mode = TransactionalWriteMode.RequireActiveTransaction)
    {
        EnableAmbientTransactionProviderRegistration = true;
        TransactionalWriteMode = mode;
        return this;
    }
}