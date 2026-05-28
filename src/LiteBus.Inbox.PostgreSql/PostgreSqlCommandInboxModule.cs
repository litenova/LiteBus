using System;
using LiteBus.Inbox.Abstractions;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.PostgreSql;

/// <summary>
///     Module for registering the PostgreSQL command inbox store.
/// </summary>
public sealed class PostgreSqlCommandInboxModule : IModule
{
    private readonly Action<PostgreSqlCommandInboxModuleBuilder> _builder;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlCommandInboxModule" /> class.
    /// </summary>
    /// <param name="builder">The module configuration action.</param>
    public PostgreSqlCommandInboxModule(Action<PostgreSqlCommandInboxModuleBuilder> builder)
    {
        _builder = builder ?? throw new ArgumentNullException(nameof(builder));
    }

    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var moduleBuilder = new PostgreSqlCommandInboxModuleBuilder();
        _builder(moduleBuilder);

        if (moduleBuilder.DataSource is null)
        {
            throw new InvalidOperationException("A PostgreSQL command inbox data source must be configured.");
        }

        var store = new PostgreSqlCommandInboxStore(moduleBuilder.DataSource, moduleBuilder.Options);

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICommandInboxWriter),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICommandInboxLeaseStore),
            store));

        configuration.DependencyRegistry.Register(new DependencyDescriptor(
            typeof(ICommandInboxStateStore),
            store));
    }
}