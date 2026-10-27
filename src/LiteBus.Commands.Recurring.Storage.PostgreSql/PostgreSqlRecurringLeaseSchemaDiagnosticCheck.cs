using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Validates that the PostgreSQL recurring leases table matches the current LiteBus schema version.
/// </summary>
public sealed class PostgreSqlRecurringLeaseSchemaDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The stable probe name reported to operators.
    /// </summary>
    public const string CheckName = "recurring.postgresql.schema";

    /// <summary>
    ///     The registered recurring lease configuration consumed by the probe.
    /// </summary>
    private readonly PostgreSqlRecurringLeaseRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringLeaseSchemaDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL recurring lease configuration.</param>
    public PostgreSqlRecurringLeaseSchemaDiagnosticCheck(PostgreSqlRecurringLeaseRegistration registration)
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
            await PostgreSqlRecurringLeaseSchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);

            return new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "PostgreSQL recurring lease schema validation succeeded.",
                new Dictionary<string, object>
                {
                    ["component"] = PostgreSqlRecurringLeaseSchema.ComponentName,
                    ["expectedVersion"] = PostgreSqlRecurringLeaseSchema.CurrentSchemaVersion,
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
                    ["component"] = PostgreSqlRecurringLeaseSchema.ComponentName,
                    ["expectedVersion"] = PostgreSqlRecurringLeaseSchema.CurrentSchemaVersion,
                    ["actualVersion"] = exception.ActualVersion ?? 0,
                    ["schemaName"] = _registration.Options.SchemaName,
                    ["tableName"] = _registration.Options.TableName
                });
        }
    }
}
