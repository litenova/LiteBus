using System;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql.Exceptions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions.Exceptions;
using Npgsql;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Module for registering the PostgreSQL inbox store.
/// </summary>
public sealed class PostgreSqlInboxModule : IInboxStorageModule, IRequires<InboxModule>
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<PostgreSqlInboxModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public PostgreSqlInboxModule(Action<PostgreSqlInboxModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new PostgreSqlInboxModuleBuilder();
        _builder(moduleBuilder);

        if (moduleBuilder.DataSource is null)
        {
            throw new InboxPostgreSqlStorageConfigurationException(
                "A PostgreSQL inbox data source must be configured. " +
                "Call UseDataSource(NpgsqlDataSource) or UseConnectionString(string).");
        }

        var dataSourceOwner = new PostgreSqlDataSourceOwner<PostgreSqlInboxModule>(moduleBuilder.DataSource, moduleBuilder.OwnsDataSource);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlDataSourceOwner<PostgreSqlInboxModule>),
            _ => dataSourceOwner,
            InstanceLifetime.Singleton));

        if (moduleBuilder.OwnsDataSource)
        {
            configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(NpgsqlDataSource), moduleBuilder.DataSource));
        }

        var store = new PostgreSqlInboxStore(moduleBuilder.DataSource, moduleBuilder.Options);
        var registration = new PostgreSqlInboxStoreRegistration(moduleBuilder.DataSource, moduleBuilder.Options);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlInboxStoreRegistration),
            registration));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxLeaseStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxStateWriter), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxDeadLetterStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxRetentionStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxDiagnosticsStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxMessageQuery), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxPurgeStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxProcessingStore), store));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxOperationsStore), store));

        if (moduleBuilder.EnableSchemaInitialization)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(PostgreSqlInboxSchemaInitializer),
                typeof(PostgreSqlInboxSchemaInitializer)));

            configuration.RegisterStartupTask(typeof(PostgreSqlInboxSchemaInitializer));
        }

        var workSignal = moduleBuilder.Options.UseListenNotify
            ? (IInboxWorkSignal) new PostgreSqlInboxWorkSignal(moduleBuilder.DataSource)
            : new InboxPollingWorkSignal();

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IInboxWorkSignal), workSignal));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlInboxSchemaDiagnosticCheck),
            typeof(PostgreSqlInboxSchemaDiagnosticCheck),
            InstanceLifetime.Singleton));

        configuration.RegisterDiagnosticCheck(typeof(PostgreSqlInboxSchemaDiagnosticCheck), "inbox.postgresql.schema");

        if (moduleBuilder.EnableAmbientTransactionProviderRegistration)
        {
            RegisterAmbientTransactionalInbox(configuration, moduleBuilder, store);
        }
    }

    /// <summary>
    ///     Registers scoped transactional inbox services resolved through the ambient PostgreSQL transaction provider.
    /// </summary>
    /// <param name="configuration">The module configuration receiving service registrations.</param>
    /// <param name="moduleBuilder">The configured PostgreSQL inbox module builder.</param>
    /// <param name="store">The singleton inbox store registered for processors and auto-commit acceptance.</param>
    private static void RegisterAmbientTransactionalInbox(
        IModuleConfiguration configuration,
        PostgreSqlInboxModuleBuilder moduleBuilder,
        PostgreSqlInboxStore store)
    {
        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlTransactionalInboxParticipant),
            serviceProvider => new PostgreSqlTransactionalInboxParticipant(
                (PostgreSqlInboxStoreRegistration) (serviceProvider.GetService(typeof(PostgreSqlInboxStoreRegistration)) ?? throw new LiteBusDependencyResolutionException(typeof(PostgreSqlInboxStoreRegistration))),
                (IInboxStore) (serviceProvider.GetService(typeof(IInboxStore)) ?? throw new LiteBusDependencyResolutionException(typeof(IInboxStore))),
                serviceProvider.GetService(typeof(IPostgreSqlTransactionProvider)) as IPostgreSqlTransactionProvider,
                moduleBuilder.TransactionalWriteMode),
            InstanceLifetime.Scoped));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransactionalInbox),
            serviceProvider =>
            {
                var participant = (PostgreSqlTransactionalInboxParticipant) (serviceProvider.GetService(typeof(PostgreSqlTransactionalInboxParticipant)) ?? throw new LiteBusDependencyResolutionException(typeof(PostgreSqlTransactionalInboxParticipant)));

                return new StoreBoundTransactionalInbox(
                    participant.ResolveStore(),
                    (IInboxEnvelopeFactory) (serviceProvider.GetService(typeof(IInboxEnvelopeFactory)) ?? throw new LiteBusDependencyResolutionException(typeof(IInboxEnvelopeFactory))));
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
                _ = services.GetService(typeof(PostgreSqlDataSourceOwner<PostgreSqlInboxModule>));
                return instance;
            },
            InstanceLifetime.Singleton);
    }
}
