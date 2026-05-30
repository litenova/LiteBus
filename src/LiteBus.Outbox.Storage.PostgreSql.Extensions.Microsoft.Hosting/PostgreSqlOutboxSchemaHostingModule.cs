using System;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Runtime.Abstractions;

namespace LiteBus.Outbox.Storage.PostgreSql.Extensions.Microsoft.Hosting;

/// <summary>
///     Registers Microsoft hosting services that bootstrap the PostgreSQL outbox schema.
/// </summary>
public sealed class PostgreSqlOutboxSchemaHostingModule : IModule, IRequires<PostgreSqlOutboxModule>
{
    /// <inheritdoc />
    public void Build(IModuleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        configuration.DependencyRegistry.RegisterHostedService(typeof(PostgreSqlOutboxSchemaHostedService));
    }
}
