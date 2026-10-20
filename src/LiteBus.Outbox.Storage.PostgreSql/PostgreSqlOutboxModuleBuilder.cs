using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Configures the PostgreSQL outbox store module.
/// </summary>
/// <example>
///     Register with an existing data source:
///     <code>
/// var dataSource = NpgsqlDataSource.Create(connectionString);
/// 
/// liteBus.AddPostgreSqlOutboxStorage(postgres =>
/// {
///     postgres.UseDataSource(dataSource);
///     postgres.UseOptions(new PostgreSqlOutboxStoreOptions { SchemaName = "app" });
/// });
///     </code>
///     Register with a connection string when the module should own the created data source:
///     <code>
/// liteBus.AddPostgreSqlOutboxStorage(postgres =>
/// {
///     postgres.UseConnectionString(configuration.GetConnectionString("OrdersDb")!);
///     postgres.EnsureSchemaCreationOnStartup();
/// });
///     </code>
/// </example>
public sealed class PostgreSqlOutboxModuleBuilder
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
    public PostgreSqlOutboxStoreOptions Options { get; private set; } = new();

    /// <summary>
    ///     Gets a value indicating whether <see cref="PostgreSqlOutboxSchemaInitializer" /> is registered.
    /// </summary>
    public bool EnableSchemaInitialization { get; private set; } = true;

    /// <summary>
    ///     Gets a value indicating whether scoped <see cref="LiteBus.Outbox.Abstractions.ITransactionalOutbox" /> is
    ///     registered
    ///     through the ambient PostgreSQL transaction provider.
    /// </summary>
    public bool EnableAmbientTransactionProviderRegistration { get; private set; }

    /// <summary>
    ///     Gets the transactional write mode used when ambient registration is enabled.
    /// </summary>
    public TransactionalWriteMode TransactionalWriteMode { get; private set; } = TransactionalWriteMode.RequireActiveTransaction;

    /// <summary>
    ///     Gets or sets the factory used to register a custom scoped transactional outbox writer.
    /// </summary>
    /// <remarks>
    ///     Companion writer packages set this through their own builder extension instead of adding public API here.
    ///     When set, <see cref="PostgreSqlOutboxModule" /> registers the factory result as scoped
    ///     <see cref="ITransactionalOutbox" />. It cannot be combined with
    ///     <see cref="EnableAmbientTransactionProvider(TransactionalWriteMode)" />.
    /// </remarks>
    internal Func<IServiceProvider, ITransactionalOutbox>? TransactionalOutboxFactory { get; set; }

    /// <summary>
    ///     Disables registration of outbox schema initialization background service work.
    /// </summary>
    /// <returns>The current builder.</returns>
    public PostgreSqlOutboxModuleBuilder DisableSchemaInitialization()
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
    ///     The outbox module does not dispose data sources supplied through this method.
    /// </remarks>
    public PostgreSqlOutboxModuleBuilder UseDataSource(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        DataSource = dataSource;
        OwnsDataSource = false;
        return this;
    }

    /// <summary>
    ///     Creates a PostgreSQL data source from a connection string and uses it for the outbox store.
    /// </summary>
    /// <param name="connectionString">
    ///     A PostgreSQL connection string, for example
    ///     <c>Host=localhost;Database=orders;Username=app;Password=secret</c>.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     The module registers the created <see cref="NpgsqlDataSource" /> with the dependency injection container so
    ///     it can be disposed on application shutdown. When inbox and outbox share one database, create one
    ///     <see cref="NpgsqlDataSource" /> and pass it to both stores with <see cref="UseDataSource(NpgsqlDataSource)" />.
    /// </remarks>
    public PostgreSqlOutboxModuleBuilder UseConnectionString(string connectionString)
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
    public PostgreSqlOutboxModuleBuilder UseOptions(PostgreSqlOutboxStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Options = options;
        return this;
    }

    /// <summary>
    ///     Enables automatic outbox schema creation or upgrade when the generic host starts.
    /// </summary>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     Schema bootstrap runs through <see cref="PostgreSqlOutboxSchemaInitializer" /> when
    ///     <see cref="EnableSchemaInitialization" /> is enabled.
    /// </remarks>
    public PostgreSqlOutboxModuleBuilder EnsureSchemaCreationOnStartup()
    {
        Options = Options with { EnsureSchemaCreationOnStartup = true };
        return this;
    }

    /// <summary>
    ///     Registers scoped <see cref="LiteBus.Outbox.Abstractions.ITransactionalOutbox" /> resolved through
    ///     <see cref="IPostgreSqlTransactionProvider" /> when the application supplies one in the current scope.
    /// </summary>
    /// <param name="mode">
    ///     Controls behavior when no ambient transaction is active. Defaults to
    ///     <see cref="TransactionalWriteMode.RequireActiveTransaction" />.
    /// </param>
    /// <returns>The current builder.</returns>
    /// <remarks>
    ///     <para>
    ///         The application owns the transaction and every writer joins it, which is what makes an ORM or document
    ///         library other than Entity Framework Core work here. A scoped unit of work opens the connection and
    ///         transaction, implements <see cref="IPostgreSqlTransactionProvider" />, and hands the same pair to the
    ///         other library: Marten takes it through <c>SessionOptions.ForTransaction</c>, Dapper takes it per
    ///         command, and raw ADO.NET already has it.
    ///     </para>
    ///     <para>
    ///         The read path is unaffected. A session or connection used for queries is resolved separately and never
    ///         opens a write transaction; only the write path goes through the unit of work. Registering one session
    ///         for both reads and writes is what would make every query hold a write transaction, and that is an
    ///         application composition choice rather than something this method requires.
    ///     </para>
    ///     <para>
    ///         <c>ITransactionalOutbox</c> binds its store when it is resolved, so a handler that injects it with no
    ///         active unit of work fails at injection under
    ///         <see cref="TransactionalWriteMode.RequireActiveTransaction" />. Prefer that failure: the alternative is
    ///         an outbox row committed on its own connection, which is the atomicity the outbox exists to provide.
    ///     </para>
    ///     <para>
    ///         See the Transactional Writes guide for the full pattern. Do not hand-write inserts against the outbox
    ///         table: its schema is created, versioned and validated by LiteBus, and a schema change would break every
    ///         such writer silently.
    ///     </para>
    /// </remarks>
    public PostgreSqlOutboxModuleBuilder EnableAmbientTransactionProvider(
        TransactionalWriteMode mode = TransactionalWriteMode.RequireActiveTransaction)
    {
        EnableAmbientTransactionProviderRegistration = true;
        TransactionalWriteMode = mode;
        return this;
    }
}
