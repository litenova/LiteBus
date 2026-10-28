using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     Validates that the PostgreSQL idempotency table matches the current LiteBus schema version.
/// </summary>
public sealed class PostgreSqlIdempotencySchemaDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The stable probe name reported to operators.
    /// </summary>
    public const string CheckName = "idempotency.postgresql.schema";

    /// <summary>
    ///     The registered idempotency store configuration consumed by the probe.
    /// </summary>
    private readonly PostgreSqlIdempotencyStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlIdempotencySchemaDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL idempotency store configuration.</param>
    public PostgreSqlIdempotencySchemaDiagnosticCheck(PostgreSqlIdempotencyStoreRegistration registration)
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
            await PostgreSqlIdempotencySchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);

            return new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "PostgreSQL idempotency schema validation succeeded.",
                new Dictionary<string, object>
                {
                    ["component"] = PostgreSqlIdempotencySchema.ComponentName,
                    ["expectedVersion"] = PostgreSqlIdempotencySchema.CurrentSchemaVersion,
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
                    ["component"] = PostgreSqlIdempotencySchema.ComponentName,
                    ["expectedVersion"] = PostgreSqlIdempotencySchema.CurrentSchemaVersion,
                    ["actualVersion"] = exception.ActualVersion ?? 0,
                    ["schemaName"] = _registration.Options.SchemaName,
                    ["tableName"] = _registration.Options.TableName
                });
        }
    }
}
