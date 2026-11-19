using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Recurring;
using LiteBus.Storage.PostgreSql;
using Npgsql;
using NpgsqlTypes;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Records recurring loop occurrences as durable rows in a PostgreSQL table.
/// </summary>
/// <remarks>
///     <para>
///         Each recording is one insert into <c>litebus_recurring_occurrences</c> that does nothing when the occurrence
///         key already exists, so two replicas racing to record the same scheduled occurrence agree on the first writer:
///         the winner gets <see langword="true" /> and the loser gets <see langword="false" />. Each read selects the
///         latest scheduled row for one loop with an optional tenant match. Every statement opens its own pooled
///         connection and no connection is held between statements, so the store works behind PgBouncer in transaction
///         mode and with pooling disabled.
///     </para>
///     <para>
///         Occurrences are stored exactly as supplied: the loop owns key stability and clock semantics, and the store
///         never rewrites the scheduled or recorded instants. A restarting loop reads the last record to decide whether
///         one catch-up pass is still due.
///     </para>
/// </remarks>
public sealed class PostgreSqlRecurringOccurrenceStore : IRecurringOccurrenceStore
{
    /// <summary>
    ///     The PostgreSQL data source used to open one connection per store statement.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     The quoted qualified occurrences table name built from lease options at construction time.
    /// </summary>
    private readonly string _tableName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringOccurrenceStore" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source used to open one connection per store statement.</param>
    /// <param name="options">The occurrence table and schema bootstrap options.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dataSource" /> is <see langword="null" />.</exception>
    public PostgreSqlRecurringOccurrenceStore(
        NpgsqlDataSource dataSource,
        PostgreSqlRecurringLeaseOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlRecurringLeaseOptions();

        _dataSource = dataSource;
        _tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.OccurrenceTableName);
    }

    /// <inheritdoc />
    public async ValueTask<bool> TryRecordAsync(RecurringOccurrence occurrence, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(occurrence);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrence.LoopName);
        ArgumentException.ThrowIfNullOrWhiteSpace(occurrence.OccurrenceKey);

        var sql = $"""
                   INSERT INTO {_tableName} (occurrence_key, loop_name, tenant_id, scheduled_at, recorded_at)
                   VALUES (@occurrenceKey, @loopName, @tenantId, @scheduledAt, @recordedAt)
                   ON CONFLICT (occurrence_key) DO NOTHING
                   RETURNING occurrence_key;
                   """;

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("occurrenceKey", occurrence.OccurrenceKey);
        command.Parameters.AddWithValue("loopName", occurrence.LoopName);
        AddNullableTextParameter(command, "tenantId", occurrence.TenantId);
        command.Parameters.AddWithValue("scheduledAt", occurrence.ScheduledAt);
        command.Parameters.AddWithValue("recordedAt", occurrence.RecordedAt);

        var recorded = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        return recorded is string;
    }

    /// <inheritdoc />
    public async ValueTask<RecurringOccurrence?> GetLastAsync(string loopName, string? tenantId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(loopName);

        // The tenant comparison is null-safe: a null tenant matches only rows recorded without a tenant, never rows
        // recorded for a named tenant. The recorded instant breaks ties when two keys share one scheduled instant.
        var sql = $"""
                   SELECT occurrence_key, loop_name, tenant_id, scheduled_at, recorded_at
                   FROM {_tableName}
                   WHERE loop_name = @loopName AND tenant_id IS NOT DISTINCT FROM @tenantId
                   ORDER BY scheduled_at DESC, recorded_at DESC
                   LIMIT 1;
                   """;

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("loopName", loopName);
        AddNullableTextParameter(command, "tenantId", tenantId);

        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        return new RecurringOccurrence(
            reader.GetString(1),
            GetNullableString(reader, 2),
            reader.GetString(0),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetFieldValue<DateTimeOffset>(4));
    }

    /// <summary>
    ///     Adds a nullable text parameter with an explicit PostgreSQL type so null checks compile in SQL.
    /// </summary>
    /// <param name="command">The command receiving the parameter.</param>
    /// <param name="name">The parameter name.</param>
    /// <param name="value">The optional text value.</param>
    private static void AddNullableTextParameter(NpgsqlCommand command, string name, string? value)
    {
        command.Parameters.Add(new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = (object?) value ?? DBNull.Value
        });
    }

    /// <summary>
    ///     Reads an optional text column.
    /// </summary>
    /// <param name="reader">The reader positioned on the row to read.</param>
    /// <param name="ordinal">The column ordinal.</param>
    /// <returns>The string when the column is not database null; otherwise, null.</returns>
    private static string? GetNullableString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }
}
