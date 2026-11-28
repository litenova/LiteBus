using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Validates that the PostgreSQL recurring occurrences table matches the current LiteBus schema version.
/// </summary>
public sealed class PostgreSqlRecurringOccurrenceSchemaDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The stable probe name reported to operators.
    /// </summary>
    public const string CheckName = "recurring.postgresql.occurrences";

    /// <summary>
    ///     The registered recurring lease configuration consumed by the probe.
    /// </summary>
    private readonly PostgreSqlRecurringLeaseRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringOccurrenceSchemaDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL recurring lease configuration.</param>
    public PostgreSqlRecurringOccurrenceSchemaDiagnosticCheck(PostgreSqlRecurringLeaseRegistration registration)
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
            await PostgreSqlRecurringOccurrenceSchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);

            return new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "PostgreSQL recurring occurrence schema validation succeeded.",
                new Dictionary<string, object>
                {
                    ["component"] = PostgreSqlRecurringOccurrenceSchema.ComponentName,
                    ["expectedVersion"] = PostgreSqlRecurringOccurrenceSchema.CurrentSchemaVersion,
                    ["schemaName"] = _registration.Options.SchemaName,
                    ["tableName"] = _registration.Options.OccurrenceTableName
                });
        }
        catch (PostgreSqlSchemaDriftException exception)
        {
            return new DiagnosticResult(
                DiagnosticStatus.Unhealthy,
                exception.Message,
                new Dictionary<string, object>
                {
                    ["component"] = PostgreSqlRecurringOccurrenceSchema.ComponentName,
                    ["expectedVersion"] = PostgreSqlRecurringOccurrenceSchema.CurrentSchemaVersion,
                    ["actualVersion"] = exception.ActualVersion ?? 0,
                    ["schemaName"] = _registration.Options.SchemaName,
                    ["tableName"] = _registration.Options.OccurrenceTableName
                });
        }
    }
}
