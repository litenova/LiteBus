using System;
using LiteBus.Inbox.PostgreSql;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Inbox.PostgreSql.Extensions.Microsoft.Hosting;

/// <summary>
///     Registers Microsoft hosting services that bootstrap the PostgreSQL command inbox schema.
/// </summary>
public sealed class PostgreSqlInboxSchemaHostingModule : IModule, IRequires<PostgreSqlCommandInboxModule>
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.RegisterHostedService(typeof(PostgreSqlInboxSchemaHostedService));
    }
}
