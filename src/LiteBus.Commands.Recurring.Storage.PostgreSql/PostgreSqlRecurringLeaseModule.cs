using System;
using LiteBus.Commands.Recurring;
using LiteBus.Runtime.Abstractions;
using LiteBus.Runtime.Abstractions.Exceptions;
using LiteBus.Storage.PostgreSql;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Module for registering the PostgreSQL recurring command lease.
/// </summary>
/// <remarks>
///     Declared as a child of <see cref="CommandModule" /> through
///     <see cref="CommandModuleBuilderPostgreSqlExtensions.UsePostgreSqlRecurringLease(CommandModuleBuilder, Action{PostgreSqlRecurringLeaseModuleBuilder})" />, so the lease is composed inside
///     <c>AddCommands</c> and cannot be registered without the axis whose loops acquire through it.
/// </remarks>
public sealed class PostgreSqlRecurringLeaseModule : IModule
{
    /// <summary>
    ///     The module builder action supplied at registration time.
    /// </summary>
    private readonly Action<PostgreSqlRecurringLeaseModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringLeaseModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    /// <exception cref="ArgumentNullException"><paramref name="builder" /> is <see langword="null" />.</exception>
    public PostgreSqlRecurringLeaseModule(Action<PostgreSqlRecurringLeaseModuleBuilder> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        _builder = builder;
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new PostgreSqlRecurringLeaseModuleBuilder();
        _builder(moduleBuilder);

        if (moduleBuilder.DataSource is null)
        {
            throw new DurableStorageConfigurationException(
                "A PostgreSQL recurring lease data source must be configured. " +
                "Call UseDataSource(NpgsqlDataSource) or UseConnectionString(string).");
        }

        moduleBuilder.Options.Validate();

        var dataSourceOwner = new PostgreSqlDataSourceOwner<PostgreSqlRecurringLeaseModule>(moduleBuilder.DataSource, moduleBuilder.OwnsDataSource);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlDataSourceOwner<PostgreSqlRecurringLeaseModule>),
            _ => dataSourceOwner,
            InstanceLifetime.Singleton));

        if (moduleBuilder.OwnsDataSource)
        {
            configuration.DependencyRegistry.Register(CreateOwnedRegistration(typeof(NpgsqlDataSource), moduleBuilder.DataSource));
        }

        var registration = new PostgreSqlRecurringLeaseRegistration(moduleBuilder.DataSource, moduleBuilder.Options);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlRecurringLeaseRegistration),
            registration));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(IRecurringCommandLease),
            services =>
            {
                var owner = (PostgreSqlDataSourceOwner<PostgreSqlRecurringLeaseModule>) services.GetService(
                    typeof(PostgreSqlDataSourceOwner<PostgreSqlRecurringLeaseModule>))!;
                var logger = (ILogger<PostgreSqlRecurringCommandLease>?) services.GetService(
                    typeof(ILogger<PostgreSqlRecurringCommandLease>));
                return new PostgreSqlRecurringCommandLease(owner.DataSource, registration.Options, logger);
            },
            InstanceLifetime.Singleton));

        if (moduleBuilder.IsSchemaInitializationEnabled)
        {
            configuration.DependencyRegistry.Register(new DependencyDescriptor(
                typeof(PostgreSqlRecurringLeaseSchemaInitializer),
                typeof(PostgreSqlRecurringLeaseSchemaInitializer)));

            configuration.RegisterStartupTask(typeof(PostgreSqlRecurringLeaseSchemaInitializer));
        }

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(PostgreSqlRecurringLeaseSchemaDiagnosticCheck),
            typeof(PostgreSqlRecurringLeaseSchemaDiagnosticCheck),
            InstanceLifetime.Singleton));

        configuration.RegisterDiagnosticCheck(
            typeof(PostgreSqlRecurringLeaseSchemaDiagnosticCheck),
            PostgreSqlRecurringLeaseSchemaDiagnosticCheck.CheckName);
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
                _ = services.GetService(typeof(PostgreSqlDataSourceOwner<PostgreSqlRecurringLeaseModule>));
                return instance;
            },
            InstanceLifetime.Singleton);
    }
}
