using LiteBus.Outbox.Abstractions;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.IntegrationTests;

internal static class EfCoreOutboxTableReaders
{
    internal static async Task<OutboxEnvelope?> ReadOutboxAsync(
        string connectionString,
        EntityFrameworkCoreOutboxStoreOptions options,
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

    private static string? ReadNullableString(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static DateTimeOffset? ReadNullableDateTimeOffset(NpgsqlDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
    }
}