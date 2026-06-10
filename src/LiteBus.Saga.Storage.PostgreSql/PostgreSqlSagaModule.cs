using LiteBus.Inbox;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Saga;
using LiteBus.Saga.Abstractions;
using Npgsql;

namespace LiteBus.Saga.Storage.PostgreSql;

/// <summary>
///     Module for registering the PostgreSQL saga store.
/// </summary>
public sealed class PostgreSqlSagaModule : ISagaStoreModule, IRequires<InboxModule>
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
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new PostgreSqlSagaModuleBuilder();
        _builder(moduleBuilder);

        if (moduleBuilder.DataSource is null)
        {
            throw new LiteBusConfigurationException(
                "A PostgreSQL saga data source must be configured. " +
                "Call UseDataSource(NpgsqlDataSource) or UseConnectionString(string).");
        }

        if (moduleBuilder.OwnsDataSource)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(NpgsqlDataSource),
                moduleBuilder.DataSource));
        }

        var registration = new PostgreSqlSagaStoreRegistration(moduleBuilder.DataSource, moduleBuilder.Options);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlSagaStoreRegistration),
            registration));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ISagaStore),
            services => new PostgreSqlSagaStore(
                registration.DataSource,
                (LiteBus.Messaging.Abstractions.IMessageSerializer)services.GetService(
                    typeof(LiteBus.Messaging.Abstractions.IMessageSerializer))!,
                registration.Options,
                services.GetService(typeof(TimeProvider)) as TimeProvider),
            InstanceLifetime.Singleton));

        configuration.SetContext(new SagaStoreRegisteredMarker());

        if (moduleBuilder.IsSchemaInitializationEnabled)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(PostgreSqlSagaSchemaInitializer),
                typeof(PostgreSqlSagaSchemaInitializer)));

            configuration.RegisterStartupTask(typeof(PostgreSqlSagaSchemaInitializer));
        }
    }

}
