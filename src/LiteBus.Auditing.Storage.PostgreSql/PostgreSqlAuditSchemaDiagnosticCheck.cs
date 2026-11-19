using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Auditing.Storage.PostgreSql;

/// <summary>
///     Validates that the PostgreSQL audit tables match the current LiteBus schema version.
/// </summary>
public sealed class PostgreSqlAuditSchemaDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The stable probe name reported to operators.
    /// </summary>
    public const string CheckName = "audit.postgresql.schema";

    /// <summary>
    ///     The registered audit store configuration consumed by the probe.
    /// </summary>
    private readonly PostgreSqlAuditStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditSchemaDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL audit store configuration.</param>
    public PostgreSqlAuditSchemaDiagnosticCheck(PostgreSqlAuditStoreRegistration registration)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registration = registration;
    }

    /// <summary>
    ///     Gets the stable probe name reported to operators.
    /// </summary>
    public string Name => CheckName;

    /// <inheritdoc />
    public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await PostgreSqlAuditSchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);

            return new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "PostgreSQL audit schema validation succeeded.",
                new Dictionary<string, object>
                {
                    ["component"] = PostgreSqlAuditSchema.ComponentName,
                    ["expectedVersion"] = PostgreSqlAuditSchema.CurrentSchemaVersion,
                    ["schemaName"] = _registration.Options.SchemaName,
                    ["recordsTableName"] = _registration.Options.TableName,
                    ["headsTableName"] = _registration.Options.HeadsTableName,
                    ["checkpointsTableName"] = _registration.Options.CheckpointsTableName
                });
        }
        catch (PostgreSqlSchemaDriftException exception)
        {
            return new DiagnosticResult(
                DiagnosticStatus.Unhealthy,
                exception.Message,
                new Dictionary<string, object>
                {
                    ["component"] = PostgreSqlAuditSchema.ComponentName,
                    ["expectedVersion"] = PostgreSqlAuditSchema.CurrentSchemaVersion,
                    ["actualVersion"] = exception.ActualVersion ?? 0,
                    ["schemaName"] = _registration.Options.SchemaName,
                    ["recordsTableName"] = _registration.Options.TableName,
                    ["headsTableName"] = _registration.Options.HeadsTableName,
                    ["checkpointsTableName"] = _registration.Options.CheckpointsTableName
                });
        }
    }
}
