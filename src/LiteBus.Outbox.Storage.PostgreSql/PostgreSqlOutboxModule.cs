using System;
using LiteBus.Outbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Outbox.Storage.PostgreSql.Exceptions;
using Npgsql;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Module for registering the PostgreSQL outbox store.
/// </summary>
public sealed class PostgreSqlOutboxModule : IModule
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
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.TryGetContext<OutboxCoreRegisteredMarker>(out _))
        {
            throw new LiteBusConfigurationException(
                $"{nameof(PostgreSqlOutboxModule)} requires OutboxModule core services " +
                "to be registered first. Configure storage inside AddOutboxModule(...) " +
                "using UsePostgreSqlStorage().");
        }

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

        var workSignal = moduleBuilder.Options.UseListenNotify
            ? (IOutboxWorkSignal)new PostgreSqlOutboxWorkSignal(moduleBuilder.DataSource)
            : new OutboxPollingWorkSignal();

        configuration.DependencyRegistry.Register(new DependencyDescriptor(typeof(IOutboxWorkSignal), workSignal));

        if (moduleBuilder.EnableSchemaInitialization)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(PostgreSqlOutboxSchemaInitializer),
                typeof(PostgreSqlOutboxSchemaInitializer)));

            configuration.RegisterStartupTask(typeof(PostgreSqlOutboxSchemaInitializer));
        }
    }
}