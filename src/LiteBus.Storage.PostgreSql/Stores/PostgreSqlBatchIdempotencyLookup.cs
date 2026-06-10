using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.Storage.PostgreSql.Stores;

/// <summary>
///     Resolves rows skipped by batched <c>ON CONFLICT DO NOTHING</c> inserts without per-row round trips.
/// </summary>
internal static class PostgreSqlBatchIdempotencyLookup
{
    /// <summary>
    ///     Identifies one attempted insert that did not return from the batch insert statement.
    /// </summary>
    /// <param name="MessageId">The attempted message identifier.</param>
    /// <param name="IdempotencyKey">The optional idempotency key supplied with the insert.</param>
    internal readonly record struct LookupKey(Guid MessageId, string? IdempotencyKey);

    /// <summary>
    ///     Loads existing rows for skipped batch inserts in one query.
    /// </summary>
    /// <typeparam name="TEnvelope">The envelope type returned to callers.</typeparam>
    /// <param name="createCommand">A factory that creates commands for the current execution scope.</param>
    /// <param name="tableName">The qualified table name used by the store.</param>
    /// <param name="selectColumnsSql">The SELECT column list shared by the store reader.</param>
    /// <param name="missingKeys">The inserts that did not appear in the batch RETURNING clause.</param>
    /// <param name="readEnvelope">Reads one envelope from the result set.</param>
    /// <param name="readMessageId">Reads the message identifier from one envelope.</param>
    /// <param name="readIdempotencyKey">Reads the idempotency key from one envelope.</param>
    /// <param name="cancellationToken">A token that cancels the lookup.</param>
    /// <returns>A dictionary keyed by attempted message identifier.</returns>
    internal static async Task<IReadOnlyDictionary<Guid, TEnvelope>> ResolveAsync<TEnvelope>(
        Func<NpgsqlCommand> createCommand,
        string tableName,
        string selectColumnsSql,
        IReadOnlyList<LookupKey> missingKeys,
        Func<NpgsqlDataReader, TEnvelope> readEnvelope,
        Func<TEnvelope, Guid> readMessageId,
        Func<TEnvelope, string?> readIdempotencyKey,
        CancellationToken cancellationToken)
    {
        if (missingKeys.Count == 0)
        {
            return new Dictionary<Guid, TEnvelope>();
        }

        var messageIds = missingKeys.Select(key => key.MessageId).Distinct().ToArray();
        var idempotencyKeys = missingKeys
            .Select(key => key.IdempotencyKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .Cast<string>()
            .ToArray();

        var sql = $"""
                  SELECT {selectColumnsSql}
                  FROM {tableName}
                  WHERE message_id = ANY(@message_ids)
                     OR (cardinality(@idempotency_keys) > 0 AND idempotency_key = ANY(@idempotency_keys));
                  """;

        await using var command = createCommand();
        command.CommandText = sql;
        PostgreSqlParameterExtensions.AddUuidArrayParameter(command, "message_ids", messageIds);
        PostgreSqlParameterExtensions.AddTextArrayParameter(command, "idempotency_keys", idempotencyKeys);

        var byMessageId = new Dictionary<Guid, TEnvelope>();
        var byIdempotencyKey = new Dictionary<string, TEnvelope>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            var envelope = readEnvelope(reader);
            var envelopeId = readMessageId(envelope);
            byMessageId[envelopeId] = envelope;

            var idempotencyKey = readIdempotencyKey(envelope);
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                byIdempotencyKey[idempotencyKey] = envelope;
            }
        }

        var resolved = new Dictionary<Guid, TEnvelope>(missingKeys.Count);
        foreach (var key in missingKeys)
        {
            if (byMessageId.TryGetValue(key.MessageId, out var byId))
            {
                resolved[key.MessageId] = byId;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(key.IdempotencyKey) &&
                byIdempotencyKey.TryGetValue(key.IdempotencyKey, out var byKey))
            {
                resolved[key.MessageId] = byKey;
            }
        }

        return resolved;
    }
}
