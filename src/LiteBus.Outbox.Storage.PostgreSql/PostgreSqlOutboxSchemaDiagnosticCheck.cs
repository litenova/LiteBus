using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Validates that the PostgreSQL outbox table matches the current LiteBus schema version.
/// </summary>
public sealed class PostgreSqlOutboxSchemaDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     Gets the stable probe name reported to operators.
    /// </summary>
    public string Name => "outbox.postgresql.schema";

    /// <summary>
    ///     The registered outbox store configuration consumed by the probe.
    /// </summary>
    private readonly PostgreSqlOutboxStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxSchemaDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL outbox store configuration.</param>
    public PostgreSqlOutboxSchemaDiagnosticCheck(PostgreSqlOutboxStoreRegistration registration)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    /// <inheritdoc />
    public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await PostgreSqlOutboxSchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);

            return new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "PostgreSQL outbox schema validation succeeded.",
                new Dictionary<string, object>
                {
                    ["component"] = PostgreSqlSchemaComponents.Outbox,
                    ["expectedVersion"] = PostgreSqlOutboxSchema.CurrentSchemaVersion,
                    ["schemaName"] = _registration.Options.SchemaName,
                    ["tableName"] = _registration.Options.TableName
                });
        }
        catch (PostgreSqlSchemaDriftException exception)
        {
            return new DiagnosticResult(
                DiagnosticStatus.Unhealthy,
                exception.Message,
                new Dictionary<string, object>
                {
                    ["component"] = PostgreSqlSchemaComponents.Outbox,
                    ["expectedVersion"] = PostgreSqlOutboxSchema.CurrentSchemaVersion,
                    ["actualVersion"] = exception.ActualVersion ?? 0,
                    ["schemaName"] = _registration.Options.SchemaName,
                    ["tableName"] = _registration.Options.TableName
                });
        }
    }
}
