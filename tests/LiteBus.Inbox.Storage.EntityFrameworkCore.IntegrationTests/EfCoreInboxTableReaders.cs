using LiteBus.Inbox.Abstractions;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

internal static class EfCoreInboxTableReaders
{
    internal static async Task<InboxEnvelope?> ReadInboxAsync(
        string connectionString,
        EntityFrameworkCoreInboxStoreOptions options,
        Guid messageId,
        CancellationToken cancellationToken = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
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

    private static string? ReadNullableString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}