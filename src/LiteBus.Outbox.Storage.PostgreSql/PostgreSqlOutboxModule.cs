using System;
using LiteBus.Outbox;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql.Exceptions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
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

        if (moduleBuilder.OwnsDataSource)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(NpgsqlDataSource),
                moduleBuilder.DataSource));
        }

        var store = new PostgreSqlOutboxStore(moduleBuilder.DataSource, moduleBuilder.Options);
        var registration = new PostgreSqlOutboxStoreRegistration(moduleBuilder.DataSource, moduleBuilder.Options);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlOutboxStoreRegistration),
            registration));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxLeaseStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxStateWriter),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDeadLetterStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxRetentionStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxDiagnosticsStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxMessageQuery),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxPurgeStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxProcessingStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IOutboxOperationsStore),
            store));

        var workSignal = moduleBuilder.Options.UseListenNotify
            ? (IOutboxWorkSignal) new PostgreSqlOutboxWorkSignal(moduleBuilder.DataSource)
            : new OutboxPollingWorkSignal();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IOutboxWorkSignal), workSignal));

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

        if (moduleBuilder.EnableAmbientTransactionProviderRegistration)
        {
            RegisterAmbientTransactionalOutbox(configuration, moduleBuilder, store);
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
                serviceProvider.GetRequiredService<PostgreSqlOutboxStoreRegistration>(),
                serviceProvider.GetRequiredService<IOutboxStore>(),
                serviceProvider.GetService(typeof(IPostgreSqlTransactionProvider)) as IPostgreSqlTransactionProvider,
                moduleBuilder.TransactionalWriteMode),
            InstanceLifetime.Scoped));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ITransactionalOutbox),
            serviceProvider =>
            {
                var participant = serviceProvider.GetRequiredService<PostgreSqlTransactionalOutboxParticipant>();

                return new StoreBoundTransactionalOutbox(
                    participant.ResolveStore(),
                    serviceProvider.GetRequiredService<IOutboxEnvelopeFactory>());
            },
            InstanceLifetime.Scoped));
    }
}
