using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Messaging.Abstractions;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Commands.Storage.PostgreSql;

/// <summary>
///     PostgreSQL-backed idempotency store with atomic claim-by-insert on the primary key.
/// </summary>
/// <remarks>
///     <para>
///         A claim inserts the key and lets the storage engine refuse the duplicate, which is the atomicity the
///         contract asks for. An insert that lands is <see cref="IdempotencyClaim.Granted(string)" />. An insert
///         refused by a row already marked applied is <see cref="IdempotencyClaim.AlreadyCompleted(string?)" /> with
///         the stored payload, or <see cref="IdempotencyClaim.FingerprintMismatch" /> when the stored fingerprint and
///         the incoming one are both set and differ. An insert refused by a row still waiting to settle is
///         <see cref="IdempotencyClaim.InFlight" />: granting it would run the work twice, and answering it would
///         report work done that might still fail.
///     </para>
///     <para>
///         An unsettled claim is held only until <see cref="PostgreSqlIdempotencyStoreOptions.ClaimTimeout" /> passes.
///         A claimant that crashes without settling would otherwise block its key until an operator intervenes, so a
///         claim older than the timeout is taken over: the new caller refreshes the row and is answered
///         <see cref="IdempotencyClaim.Granted(string)" />. The takeover compares the <c>claimed_at</c> value it read,
///         so two callers racing to take over the same expired claim cannot both win.
///     </para>
///     <para>
///         Every grant carries a token fenced into the row, and a settle lands only while the row still carries it. A
///         completion or release that arrives after a takeover is a no-op instead of marking the new claimant's work
///         applied or freeing its live claim. Completing a key the caller does not hold inserts nothing, so a late
///         settle cannot leave an applied row with no fingerprint behind.
///     </para>
///     <para>
///         Nothing here expires an applied key. The contract leaves retention to the application because only the
///         application knows how long a repeat is still plausible. Purge applied rows with a retention job once repeats
///         stop being plausible; the <c>completed_at</c> column exists so that job has a cutoff.
///     </para>
///     <para>
///         Each call runs on its own connection, so the claim is not atomic with the change the handler applies. A
///         crash between the claim commit and the handler commit leaves the key claimed for work that never happened,
///         and the completion handler settles the key only when the mediation ends inside this process. Commands whose
///         handler joins an application transaction should claim through that same unit of work with a store that can
///         see it; this store suits commands applied without one.
///     </para>
/// </remarks>
public sealed class PostgreSqlIdempotencyStore : IIdempotencyStore
{
    /// <summary>
    ///     How long an unsettled claim blocks its key before a new caller takes it over.
    /// </summary>
    private readonly TimeSpan _claimTimeout;

    /// <summary>
    ///     The PostgreSQL data source used to open commands against the idempotency keys table.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     The quoted qualified idempotency keys table name built from store options at construction time.
    /// </summary>
    private readonly string _tableName;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlIdempotencyStore" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="options">The idempotency store options.</param>
    /// <exception cref="ArgumentOutOfRangeException"><c>options</c> carries a claim timeout that is zero or negative.</exception>
    public PostgreSqlIdempotencyStore(
        NpgsqlDataSource dataSource,
        PostgreSqlIdempotencyStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlIdempotencyStoreOptions();
        options.Validate();

        _dataSource = dataSource;
        _claimTimeout = options.ClaimTimeout;
        _tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
    }

    /// <inheritdoc />
    public async Task<IdempotencyClaim> TryClaimAsync(string key, string? fingerprint, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // The insert is the atomic claim. A refused insert reads the winner; a winner that vanished between the two
        // was released concurrently, so the loop claims the freed key instead of reporting a conflict for a row that
        // no longer exists.
        while (true)
        {
            var claimToken = Guid.NewGuid().ToString("N");

            var insertSql = $"""
                             INSERT INTO {_tableName} (idempotency_key, fingerprint, is_completed, claim_token)
                             VALUES (@key, @fingerprint, false, @claimToken)
                             ON CONFLICT DO NOTHING;
                             """;

            using var insertCommand = new NpgsqlCommand(insertSql, connection);
            insertCommand.Parameters.AddWithValue("key", key);
            insertCommand.Parameters.AddWithValue("fingerprint", (object?) fingerprint ?? DBNull.Value);
            insertCommand.Parameters.AddWithValue("claimToken", claimToken);
            var inserted = await insertCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (inserted == 1)
            {
                return IdempotencyClaim.Granted(claimToken);
            }

            string? payload;
            bool isCompleted;
            string? storedFingerprint;
            DateTimeOffset claimedAt;
            bool isExpired;

            {
                var selectSql = $"""
                                 SELECT payload, is_completed, fingerprint, claimed_at,
                                     claimed_at < now() - @timeout AS is_expired
                                 FROM {_tableName}
                                 WHERE idempotency_key = @key
                                 LIMIT 1;
                                 """;

                using var selectCommand = new NpgsqlCommand(selectSql, connection);
                selectCommand.Parameters.AddWithValue("key", key);
                selectCommand.Parameters.AddWithValue("timeout", _claimTimeout);
                using var reader = await selectCommand.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

                if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    continue;
                }

                payload = reader.IsDBNull(0) ? null : reader.GetString(0);
                isCompleted = reader.GetBoolean(1);
                storedFingerprint = reader.IsDBNull(2) ? null : reader.GetString(2);
                claimedAt = reader.GetFieldValue<DateTimeOffset>(3);
                isExpired = reader.GetBoolean(4);
            }

            if (isCompleted)
            {
                if (storedFingerprint is not null
                    && fingerprint is not null
                    && !string.Equals(storedFingerprint, fingerprint, StringComparison.Ordinal))
                {
                    return IdempotencyClaim.FingerprintMismatch;
                }

                return IdempotencyClaim.AlreadyCompleted(payload);
            }

            if (!isExpired)
            {
                return IdempotencyClaim.InFlight;
            }

            // The takeover refreshes the expired claim only when the row still carries the claimed_at this caller
            // read. A caller that lost the race to another takeover, a completion, or a release updates nothing and
            // loops back to read what the winner left. The new token fences the refreshed claim, so the superseded
            // claimant's late settle no longer matches.
            var takeoverToken = Guid.NewGuid().ToString("N");

            var takeoverSql = $"""
                               UPDATE {_tableName}
                               SET fingerprint = @fingerprint, claimed_at = now(), claim_token = @claimToken
                               WHERE idempotency_key = @key
                                   AND is_completed = false
                                   AND claimed_at = @claimedAt;
                               """;

            using var takeoverCommand = new NpgsqlCommand(takeoverSql, connection);
            takeoverCommand.Parameters.AddWithValue("key", key);
            takeoverCommand.Parameters.AddWithValue("fingerprint", (object?) fingerprint ?? DBNull.Value);
            takeoverCommand.Parameters.AddWithValue("claimToken", takeoverToken);
            takeoverCommand.Parameters.AddWithValue("claimedAt", claimedAt);
            var taken = await takeoverCommand.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

            if (taken == 1)
            {
                return IdempotencyClaim.Granted(takeoverToken);
            }
        }
    }

    /// <inheritdoc />
    public async Task CompleteAsync(string key, string claimToken, string? payload, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

        // Only the holder completes. A completion carrying a token the row no longer carries after a takeover updates
        // nothing instead of marking the new claimant's work applied, and a completion for a missing key inserts
        // nothing instead of leaving an applied row with no fingerprint behind.
        var sql = $"""
                   UPDATE {_tableName}
                   SET payload = @payload, is_completed = true, completed_at = now()
                   WHERE idempotency_key = @key
                       AND claim_token = @claimToken;
                   """;

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("claimToken", claimToken);
        command.Parameters.AddWithValue("payload", (object?) payload ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ReleaseAsync(string key, string claimToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(claimToken);

        // Only an unsettled claim still carrying the token is removed. A completed record is never deleted here, so
        // a release racing a completion cannot turn an applied key back into a claimable one, and a late release from
        // a superseded claimant cannot free the new claimant's live claim.
        var sql = $"""
                   DELETE FROM {_tableName}
                   WHERE idempotency_key = @key
                       AND is_completed = false
                       AND claim_token = @claimToken;
                   """;

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("claimToken", claimToken);

        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }
}
