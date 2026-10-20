using System;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql.Exceptions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Exceptions;
using Npgsql;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Module for registering the PostgreSQL outbox store.
/// </summary>
public sealed class PostgreSqlOutboxModule : IOutboxStorageModule, IRequires<OutboxModule>
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<PostgreSqlOutboxModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public PostgreSqlOutboxModule(Action<PostgreSqlOutboxModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new PostgreSqlOutboxModuleBuilder();
        _builder(moduleBuilder);

        if (moduleBuilder.DataSource is null)
        {
            throw new OutboxPostgreSqlStorageConfigurationException(
                "A PostgreSQL outbox data source must be configured. " +
                "Call UseDataSource(NpgsqlDataSource) or UseConnectionString(string).");
        }

        var dataSourceOwner = new PostgreSqlDataSourceOwner<PostgreSqlOutboxModule>(moduleBuilder.DataSource, moduleBuilder.OwnsDataSource);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlDataSourceOwner<PostgreSqlOutboxModule>),
            _ => dataSourceOwner,
            InstanceLifetime.Singleton));

        if (moduleBuilder.OwnsDataSource)
        {
            configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(NpgsqlDataSource), moduleBuilder.DataSource));
        }

        var store = new PostgreSqlOutboxStore(moduleBuilder.DataSource, moduleBuilder.Options);
        var registration = new PostgreSqlOutboxStoreRegistration(moduleBuilder.DataSource, moduleBuilder.Options);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlOutboxStoreRegistration),
            registration));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxLeaseStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxStateWriter), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxDeadLetterStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxRetentionStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxDiagnosticsStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxMessageQuery), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxPurgeStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxProcessingStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxOperationsStore), store));

        var workSignal = moduleBuilder.Options.UseListenNotify
            ? (IOutboxWorkSignal) new PostgreSqlOutboxWorkSignal(moduleBuilder.DataSource)
            : new OutboxPollingWorkSignal();

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IOutboxWorkSignal), workSignal));

        if (moduleBuilder.EnableSchemaInitialization)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(PostgreSqlOutboxSchemaInitializer),
                typeof(PostgreSqlOutboxSchemaInitializer)));

            configuration.RegisterStartupTask(typeof(PostgreSqlOutboxSchemaInitializer));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlOutboxSchemaDiagnosticCheck),
            typeof(PostgreSqlOutboxSchemaDiagnosticCheck),
            InstanceLifetime.Singleton));

        configuration.RegisterDiagnosticCheck(typeof(PostgreSqlOutboxSchemaDiagnosticCheck), "outbox.postgresql.schema");

        if (moduleBuilder.EnableAmbientTransactionProviderRegistration && moduleBuilder.TransactionalOutboxFactory is not null)
        {
            throw new OutboxPostgreSqlStorageConfigurationException(
                "EnableAmbientTransactionProvider() and UseMartenTransactionalOutbox() both register ITransactionalOutbox. " +
                "Call only one of them: the ambient provider when the application owns the transaction, or the Marten queued writer when one Marten session serves reads and writes.");
        }

        if (moduleBuilder.EnableAmbientTransactionProviderRegistration)
        {
            RegisterAmbientTransactionalOutbox(configuration, moduleBuilder, store);
        }

        if (moduleBuilder.TransactionalOutboxFactory is { } transactionalOutboxFactory)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(ITransactionalOutbox),
                transactionalOutboxFactory,
                InstanceLifetime.Scoped));
        }
    }

    /// <summary>
    ///     Registers scoped transactional outbox services resolved through the ambient PostgreSQL transaction provider.
    /// </summary>
    /// <param name="configuration">The module configuration receiving service registrations.</param>
    /// <param name="moduleBuilder">The configured PostgreSQL outbox module builder.</param>
    /// <param name="store">The singleton outbox store registered for processors and auto-commit enqueue.</param>
    private static void RegisterAmbientTransactionalOutbox(
        IModuleConfiguration configuration,
        PostgreSqlOutboxModuleBuilder moduleBuilder,
        PostgreSqlOutboxStore store)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlTransactionalOutboxParticipant),
            serviceProvider => new PostgreSqlTransactionalOutboxParticipant(
                (PostgreSqlOutboxStoreRegistration) (serviceProvider.GetService(typeof(PostgreSqlOutboxStoreRegistration)) ?? throw new LiteBusDependencyResolutionException(typeof(PostgreSqlOutboxStoreRegistration))),
                (IOutboxStore) (serviceProvider.GetService(typeof(IOutboxStore)) ?? throw new LiteBusDependencyResolutionException(typeof(IOutboxStore))),
                serviceProvider.GetService(typeof(IPostgreSqlTransactionProvider)) as IPostgreSqlTransactionProvider,
                moduleBuilder.TransactionalWriteMode),
            InstanceLifetime.Scoped));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransactionalOutbox),
            serviceProvider =>
            {
                var participant = (PostgreSqlTransactionalOutboxParticipant) (serviceProvider.GetService(typeof(PostgreSqlTransactionalOutboxParticipant)) ?? throw new LiteBusDependencyResolutionException(typeof(PostgreSqlTransactionalOutboxParticipant)));

                return new StoreBoundTransactionalOutbox(
                    participant.ResolveStore(),
                    (IOutboxEnvelopeFactory) (serviceProvider.GetService(typeof(IOutboxEnvelopeFactory)) ?? throw new LiteBusDependencyResolutionException(typeof(IOutboxEnvelopeFactory))));
            },
            InstanceLifetime.Scoped));
    }

    /// <summary>
    ///     Creates a singleton registration that resolves the module's data source owner before returning
    ///     <paramref name="instance" />.
    /// </summary>
    /// <param name="dependencyType">The service type to register.</param>
    /// <param name="instance">The prebuilt service the registration returns.</param>
    /// <returns>A singleton factory registration.</returns>
    /// <remarks>
    ///     Resolving the owner from every store registration makes the container create and capture it the first time
    ///     any of the module's services is used, so disposing the container disposes a data source the module created.
    ///     A prebuilt instance registration would not: Microsoft dependency injection never disposes one.
    /// </remarks>
    private static DependencyDescriptor CreateOwnedRegistration(Type dependencyType, object instance)
    {
        return new DependencyDescriptor(
            dependencyType,
            services =>
            {
                _ = services.GetService(typeof(PostgreSqlDataSourceOwner<PostgreSqlOutboxModule>));
                return instance;
            },
            InstanceLifetime.Singleton);
    }
}
