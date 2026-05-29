using System;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.PostgreSql;

/// <summary>
///     Acquires and releases a PostgreSQL session advisory lock used during schema bootstrap.
/// </summary>
internal sealed class PostgreSqlAdvisoryLockScope : IAsyncDisposable
{
    private readonly NpgsqlConnection _connection;
    private readonly int _key1;
    private readonly int _key2;
    private bool _acquired;

    private PostgreSqlAdvisoryLockScope(NpgsqlConnection connection, int key1, int key2, bool acquired)
    {
        _connection = connection;
        _key1 = key1;
        _key2 = key2;
        _acquired = acquired;
    }

    /// <summary>
    ///     Attempts to acquire a session advisory lock for the supplied key.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="lockKey">The stable lock key.</param>
    /// <param name="cancellationToken">A token used to cancel the attempt.</param>
    /// <returns>
    ///     A scope that releases the lock on disposal when acquisition succeeded; otherwise <see langword="null" />.
    /// </returns>
    public static async Task<PostgreSqlAdvisoryLockScope?> TryAcquireAsync(
        NpgsqlConnection connection,
        string lockKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);

        var (key1, key2) = CreateLockKeys(lockKey);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_try_advisory_lock(@key1, @key2);";
        command.Parameters.AddWithValue("key1", key1);
        command.Parameters.AddWithValue("key2", key2);

        var acquired = (bool)(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) ?? false);
        return acquired ? new PostgreSqlAdvisoryLockScope(connection, key1, key2, acquired: true) : null;
    }

    /// <summary>
    ///     Waits until the advisory lock can be acquired or the timeout elapses.
    /// </summary>
    /// <param name="connection">The open PostgreSQL connection.</param>
    /// <param name="lockKey">The stable lock key.</param>
    /// <param name="timeout">The maximum time to wait for the lock.</param>
    /// <param name="pollInterval">The delay between lock attempts.</param>
    /// <param name="cancellationToken">A token used to cancel the wait.</param>
    /// <returns>A scope that releases the lock on disposal.</returns>
    public static async Task<PostgreSqlAdvisoryLockScope> AcquireAsync(
        NpgsqlConnection connection,
        string lockKey,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentException.ThrowIfNullOrWhiteSpace(lockKey);

        var deadline = DateTime.UtcNow + timeout;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var scope = await TryAcquireAsync(connection, lockKey, cancellationToken).ConfigureAwait(false);
            if (scope is not null)
            {
                return scope;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Timed out after {timeout} waiting for PostgreSQL advisory lock '{lockKey}'.");
            }

            await Task.Delay(pollInterval, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (!_acquired)
        {
            return;
        }

        _acquired = false;

        await using var command = _connection.CreateCommand();
        command.CommandText = "SELECT pg_advisory_unlock(@key1, @key2);";
        command.Parameters.AddWithValue("key1", _key1);
        command.Parameters.AddWithValue("key2", _key2);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    internal static (int Key1, int Key2) CreateLockKeys(string lockKey)
    {
        var hash = PostgreSqlIdentifier.StableHash(lockKey);
        var key1 = (int)(hash & 0x7FFFFFFF);
        var key2 = (int)((hash >> 16) & 0x7FFFFFFF);
        return (key1, key2);
    }
}
