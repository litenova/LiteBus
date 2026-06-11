using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Runtime.Abstractions.Diagnostics;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Validates that the PostgreSQL inbox table matches the current LiteBus schema version.
/// </summary>
public sealed class PostgreSqlInboxSchemaDiagnosticCheck : IDiagnosticCheck
{
    /// <summary>
    ///     The registered inbox store configuration consumed by the probe.
    /// </summary>
    private readonly PostgreSqlInboxStoreRegistration _registration;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxSchemaDiagnosticCheck" /> class.
    /// </summary>
    /// <param name="registration">The registered PostgreSQL inbox store configuration.</param>
    public PostgreSqlInboxSchemaDiagnosticCheck(PostgreSqlInboxStoreRegistration registration)
    {
        _registration = registration ?? throw new ArgumentNullException(nameof(registration));
    }

    /// <summary>
    ///     Gets the stable probe name reported to operators.
    /// </summary>
    public string Name => "inbox.postgresql.schema";

    /// <inheritdoc />
    public async Task<DiagnosticResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await PostgreSqlInboxSchema.ValidateAsync(
                    _registration.DataSource,
                    _registration.Options,
                    cancellationToken)
                .ConfigureAwait(false);

            return new DiagnosticResult(
                DiagnosticStatus.Healthy,
                "PostgreSQL inbox schema validation succeeded.",
                new Dictionary<string, object>
                {
                    ["component"] = PostgreSqlSchemaComponents.Inbox,
                    ["expectedVersion"] = PostgreSqlInboxSchema.CurrentSchemaVersion,
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
                    ["component"] = PostgreSqlSchemaComponents.Inbox,
                    ["expectedVersion"] = PostgreSqlInboxSchema.CurrentSchemaVersion,
                    ["actualVersion"] = exception.ActualVersion ?? 0,
                    ["schemaName"] = _registration.Options.SchemaName,
                    ["tableName"] = _registration.Options.TableName
                });
        }
    }
}