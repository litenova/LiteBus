using System;
using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql.Exceptions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
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

        if (moduleBuilder.OwnsDataSource)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(NpgsqlDataSource),
                moduleBuilder.DataSource));
        }

        var store = new PostgreSqlInboxStore(moduleBuilder.DataSource, moduleBuilder.Options);
        var registration = new PostgreSqlInboxStoreRegistration(moduleBuilder.DataSource, moduleBuilder.Options);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlInboxStoreRegistration),
            registration));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxLeaseStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxStateWriter),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxDeadLetterStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxRetentionStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxDiagnosticsStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxMessageQuery),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxPurgeStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxProcessingStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IInboxOperationsStore),
            store));

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

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IInboxWorkSignal), workSignal));

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
                serviceProvider.GetRequiredService<PostgreSqlInboxStoreRegistration>(),
                serviceProvider.GetRequiredService<IInboxStore>(),
                serviceProvider.GetService(typeof(IPostgreSqlTransactionProvider)) as IPostgreSqlTransactionProvider,
                moduleBuilder.TransactionalWriteMode),
            InstanceLifetime.Scoped));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransactionalInbox),
            serviceProvider =>
            {
                var participant = serviceProvider.GetRequiredService<PostgreSqlTransactionalInboxParticipant>();

                return new StoreBoundTransactionalInbox(
                    participant.ResolveStore(),
                    serviceProvider.GetRequiredService<IInboxEnvelopeFactory>());
            },
            InstanceLifetime.Scoped));
    }
}
