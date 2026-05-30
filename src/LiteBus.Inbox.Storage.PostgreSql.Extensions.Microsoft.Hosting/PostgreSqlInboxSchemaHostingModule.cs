using System;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.Storage.PostgreSql.Extensions.Microsoft.Hosting;

/// <summary>
///     Registers Microsoft hosting services that bootstrap the PostgreSQL command inbox schema.
/// </summary>
public sealed class PostgreSqlInboxSchemaHostingModule : IModule, IRequires<PostgreSqlInboxModule>
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.RegisterHostedService(typeof(PostgreSqlInboxSchemaHostedService));
    }
}
