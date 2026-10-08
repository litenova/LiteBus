using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Saga.Abstractions;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Module for registering the PostgreSQL saga store.
/// </summary>
public sealed class PostgreSqlSagaModule : ISagaStorageModule
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<PostgreSqlSagaModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSagaModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public PostgreSqlSagaModule(Action<PostgreSqlSagaModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new PostgreSqlSagaModuleBuilder();
        _builder(moduleBuilder);

        if (moduleBuilder.DataSource is null)
        {
            throw new DurableStorageConfigurationException(
                "A PostgreSQL saga data source must be configured. " +
                "Call UseDataSource(NpgsqlDataSource) or UseConnectionString(string).");
        }

        var dataSourceOwner = new PostgreSqlDataSourceOwner<PostgreSqlSagaModule>(moduleBuilder.DataSource, moduleBuilder.OwnsDataSource);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlDataSourceOwner<PostgreSqlSagaModule>),
            _ => dataSourceOwner,
            InstanceLifetime.Singleton));

        if (moduleBuilder.OwnsDataSource)
        {
            configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(NpgsqlDataSource), moduleBuilder.DataSource));
        }

        var registration = new PostgreSqlSagaStoreRegistration(moduleBuilder.DataSource, moduleBuilder.Options);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlSagaStoreRegistration),
            registration));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ISagaStore),
            services => new PostgreSqlSagaStore(
                ((PostgreSqlDataSourceOwner<PostgreSqlSagaModule>) services.GetService(
                    typeof(PostgreSqlDataSourceOwner<PostgreSqlSagaModule>))!).DataSource,
                (IMessageSerializer) services.GetService(
                    typeof(IMessageSerializer))!,
                registration.Options,
                services.GetService(typeof(TimeProvider)) as TimeProvider),
            InstanceLifetime.Singleton));

        if (moduleBuilder.IsSchemaInitializationEnabled)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(PostgreSqlSagaSchemaInitializer),
                typeof(PostgreSqlSagaSchemaInitializer)));

            configuration.RegisterStartupTask(typeof(PostgreSqlSagaSchemaInitializer));
        }
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
                _ = services.GetService(typeof(PostgreSqlDataSourceOwner<PostgreSqlSagaModule>));
                return instance;
            },
            InstanceLifetime.Singleton);
    }
}
