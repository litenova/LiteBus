using System.Globalization;
using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Saga.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

internal static class PostgreSqlTableReaders
{
    internal static async Task<InboxEnvelope?> ReadInboxAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlInboxStoreOptions options,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
                               SELECT
                                   message_id,
                                   contract_name,
                                   contract_version,
                                   payload::text,
                                   created_at,
                                   visible_after,
                                   attempt_count,
                                   status,
                                   idempotency_key,
                                   lease_owner,
                                   lease_expires_at,
                                   last_error,
                                   correlation_id,
                                   causation_id,
                                   tenant_id,
                                   trace_context::text,
                                   completed_at
                               FROM {tableName}
                               WHERE message_id = @message_id;
                               """;

        command.Parameters.AddWithValue("message_id", messageId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new InboxEnvelope
        {
            Id = reader.GetGuid(0),
            ContractName = reader.GetString(1),
            ContractVersion = reader.GetInt32(2),
            Payload = reader.GetString(3),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(4),
            VisibleAfter = ReadNullableDateTimeOffset(reader, 5),
            AttemptCount = reader.GetInt32(6),
            Status = (InboxStatus) reader.GetInt32(7),
            IdempotencyKey = ReadNullableString(reader, 8),
            LeaseOwner = ReadNullableString(reader, 9),
            LeaseExpiresAt = ReadNullableDateTimeOffset(reader, 10),
            LastError = ReadNullableString(reader, 11),
            CorrelationId = ReadNullableString(reader, 12),
            CausationId = ReadNullableString(reader, 13),
            TenantId = ReadNullableString(reader, 14),
            TraceContext = ReadNullableString(reader, 15),
            CompletedAt = ReadNullableDateTimeOffset(reader, 16)
        };
    }

    internal static async Task<OutboxEnvelope?> ReadOutboxAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlOutboxStoreOptions options,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
                               SELECT
                                   message_id,
                                   contract_name,
                                   contract_version,
                                   payload::text,
                                   topic,
                                   created_at,
                                   visible_after,
                                   status,
                                   attempt_count,
                                   lease_owner,
                                   lease_expires_at,
                                   last_error,
                                   correlation_id,
                                   causation_id,
                                   tenant_id
                               FROM {tableName}
                               WHERE message_id = @message_id;
                               """;

        command.Parameters.AddWithValue("message_id", messageId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new OutboxEnvelope
        {
            Id = reader.GetGuid(0),
            ContractName = reader.GetString(1),
            ContractVersion = reader.GetInt32(2),
            Payload = reader.GetString(3),
            Topic = ReadNullableString(reader, 4),
            CreatedAt = reader.GetFieldValue<DateTimeOffset>(5),
            VisibleAfter = ReadNullableDateTimeOffset(reader, 6),
            Status = (OutboxStatus) reader.GetInt32(7),
            AttemptCount = reader.GetInt32(8),
            LeaseOwner = ReadNullableString(reader, 9),
            LeaseExpiresAt = ReadNullableDateTimeOffset(reader, 10),
            LastError = ReadNullableString(reader, 11),
            CorrelationId = ReadNullableString(reader, 12),
            CausationId = ReadNullableString(reader, 13),
            TenantId = ReadNullableString(reader, 14)
        };
    }

    internal static async Task<int> CountInboxRowsAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlInboxStoreOptions options,
        CancellationToken cancellationToken = default)
    {
        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    internal static async Task<SagaTableRow?> ReadSagaAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlSagaStoreOptions options,
        string correlationId,
        string sagaType,
        CancellationToken cancellationToken = default)
    {
        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
                               SELECT
                                   correlation_id,
                                   saga_type,
                                   state_json::text,
                                   optimistic_lock_version,
                                   is_completed
                               FROM {tableName}
                               WHERE correlation_id = @correlation_id
                                   AND saga_type = @saga_type;
                               """;

        command.Parameters.AddWithValue("correlation_id", correlationId);
        command.Parameters.AddWithValue("saga_type", sagaType);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SagaTableRow
        {
            CorrelationId = reader.GetString(0),
            SagaType = reader.GetString(1),
            StateJson = reader.GetString(2),
            OptimisticLockVersion = reader.GetInt32(3),
            IsCompleted = reader.GetBoolean(4)
        };
    }

    internal static async Task<int> CountSagaRowsAsync(
        NpgsqlDataSource dataSource,
        PostgreSqlSagaStoreOptions options,
        CancellationToken cancellationToken = default)
    {
        var tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {tableName};";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private static string? ReadNullableString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }

    internal sealed class SagaTableRow
    {
        public required string CorrelationId { get; init; }

        public required string SagaType { get; init; }

        public required string StateJson { get; init; }

        public required int OptimisticLockVersion { get; init; }

        public required bool IsCompleted { get; init; }
    }
}