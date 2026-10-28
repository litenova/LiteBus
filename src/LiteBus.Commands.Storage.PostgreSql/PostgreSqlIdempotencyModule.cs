using System;
using LiteBus.Messaging.Abstractions;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     Module for registering the PostgreSQL idempotency store.
/// </summary>
/// <remarks>
///     Declared as a child of <see cref="CommandModule" /> through
///     <see cref="CommandModuleBuilderPostgreSqlExtensions.UsePostgreSqlIdempotencyStore(CommandModuleBuilder, Action{PostgreSqlIdempotencyModuleBuilder})" />, so the store is composed inside
///     <c>AddCommands</c> and cannot be registered without the axis whose shortcuts claim through it.
/// </remarks>
public sealed class PostgreSqlIdempotencyModule : IModule, IRequires<CommandModule>
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<PostgreSqlIdempotencyModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlIdempotencyModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public PostgreSqlIdempotencyModule(Action<PostgreSqlIdempotencyModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new PostgreSqlIdempotencyModuleBuilder();
        _builder(moduleBuilder);

        if (moduleBuilder.DataSource is null)
        {
            throw new DurableStorageConfigurationException(
                "A PostgreSQL idempotency data source must be configured. " +
                "Call UseDataSource(NpgsqlDataSource) or UseConnectionString(string).");
        }

        moduleBuilder.Options.Validate();

        var dataSourceOwner = new PostgreSqlDataSourceOwner<PostgreSqlIdempotencyModule>(moduleBuilder.DataSource, moduleBuilder.OwnsDataSource);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlDataSourceOwner<PostgreSqlIdempotencyModule>),
            _ => dataSourceOwner,
            InstanceLifetime.Singleton));

        if (moduleBuilder.OwnsDataSource)
        {
            configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(NpgsqlDataSource), moduleBuilder.DataSource));
        }

        var store = new PostgreSqlIdempotencyStore(moduleBuilder.DataSource, moduleBuilder.Options);
        var registration = new PostgreSqlIdempotencyStoreRegistration(moduleBuilder.DataSource, moduleBuilder.Options);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlIdempotencyStoreRegistration),
            registration));

        configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(IIdempotencyStore), store));

        if (moduleBuilder.IsSchemaInitializationEnabled)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(PostgreSqlIdempotencySchemaInitializer),
                typeof(PostgreSqlIdempotencySchemaInitializer)));

            configuration.RegisterStartupTask(typeof(PostgreSqlIdempotencySchemaInitializer));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlIdempotencySchemaDiagnosticCheck),
            typeof(PostgreSqlIdempotencySchemaDiagnosticCheck),
            InstanceLifetime.Singleton));

        configuration.RegisterDiagnosticCheck(
            typeof(PostgreSqlIdempotencySchemaDiagnosticCheck),
            PostgreSqlIdempotencySchemaDiagnosticCheck.CheckName);
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
                _ = services.GetService(typeof(PostgreSqlDataSourceOwner<PostgreSqlIdempotencyModule>));
                return instance;
            },
            InstanceLifetime.Singleton);
    }
}
