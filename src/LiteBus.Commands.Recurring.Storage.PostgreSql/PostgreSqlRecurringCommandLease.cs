using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Commands.Recurring;
using LiteBus.Storage.PostgreSql;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace LiteBus.Commands.Recurring.Storage.PostgreSql;

/// <summary>
///     Acquires recurring command single-flight leases as renewable rows in a PostgreSQL table.
/// </summary>
/// <remarks>
///     <para>
///         Each acquisition is one upsert against <c>litebus_recurring_leases</c>: the row is inserted when absent, or
///         taken over when its <c>expires_at</c> has passed on the database clock. The statement returns a row only to
///         the winner, so a second holder racing the same name gets <see langword="null" />. Every timestamp comes from
///         <c>now()</c>; host clocks are never read, so skew between replicas cannot shorten or extend a lease.
///     </para>
///     <para>
///         The returned handle renews the row in the background while it is held and deletes it on dispose, but only
///         while this holder still owns it. A renewal that finds the lease taken or fails cancels
///         <see cref="IRecurringCommandLeaseHandle.LeaseLost" /> on the handle, so the pass holding it ends its busy
///         period instead of running undefended. A holder whose process dies stops renewing, so its row becomes takeable
///         once the lease duration lapses; no connection state is involved. Each statement opens its own pooled
///         connection and no connection is held between statements, so the lease works behind PgBouncer in transaction
///         mode and with pooling disabled.
///     </para>
///     <para>
///         The holder identity is the machine name plus the process id plus a per-instance GUID, recorded on every row
///         so operators can see which replica holds each lease.
///     </para>
/// </remarks>
public sealed class PostgreSqlRecurringCommandLease : IRecurringCommandLease
{
    /// <summary>
    ///     The PostgreSQL data source used to open one connection per lease statement.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     The quoted qualified leases table name built from lease options at construction time.
    /// </summary>
    private readonly string _tableName;

    /// <summary>
    ///     How long one acquisition holds the lease before it must be renewed.
    /// </summary>
    private readonly TimeSpan _leaseDuration;

    /// <summary>
    ///     How often a held lease is renewed while its handle is alive.
    /// </summary>
    private readonly TimeSpan _renewInterval;

    /// <summary>
    ///     The holder identity recorded on rows this instance acquires.
    /// </summary>
    private readonly string _holder;

    /// <summary>
    ///     Records renewal failures and stolen leases.
    /// </summary>
    private readonly ILogger<PostgreSqlRecurringCommandLease> _logger;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlRecurringCommandLease" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source used to open one connection per lease statement.</param>
    /// <param name="options">The lease table, schema bootstrap, and timing options.</param>
    /// <param name="logger">Records renewal failures and stolen leases, or <see langword="null" /> to discard them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="dataSource" /> is <see langword="null" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    ///     The configured lease duration or effective renewal interval is zero or negative.
    /// </exception>
    public PostgreSqlRecurringCommandLease(
        NpgsqlDataSource dataSource,
        PostgreSqlRecurringLeaseOptions? options = null,
        ILogger<PostgreSqlRecurringCommandLease>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        options ??= new PostgreSqlRecurringLeaseOptions();
        options.Validate();

        _dataSource = dataSource;
        _tableName = PostgreSqlIdentifier.Qualify(options.SchemaName, options.TableName);
        _leaseDuration = options.LeaseDuration;
        _renewInterval = options.EffectiveRenewInterval;
        _holder = CreateHolderIdentity();
        _logger = logger ?? NullLogger<PostgreSqlRecurringCommandLease>.Instance;
    }

    /// <inheritdoc />
    public async Task<IRecurringCommandLeaseHandle?> TryAcquireAsync(string leaseName, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);

        var sql = $"""
                   INSERT INTO {_tableName} (lease_name, holder, acquired_at, expires_at, renewed_at)
                   VALUES (@leaseName, @holder, now(), now() + @leaseDuration, now())
                   ON CONFLICT (lease_name) DO UPDATE
                   SET holder = EXCLUDED.holder,
                       acquired_at = EXCLUDED.acquired_at,
                       expires_at = EXCLUDED.expires_at,
                       renewed_at = EXCLUDED.renewed_at
                   WHERE {_tableName}.expires_at <= now()
                   RETURNING lease_name;
                   """;

        using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("leaseName", leaseName);
        command.Parameters.AddWithValue("holder", _holder);
        command.Parameters.AddWithValue("leaseDuration", _leaseDuration);

        var winner = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

        if (winner is not string)
        {
            return null;
        }

        return new LeaseHandle(_dataSource, _tableName, leaseName, _holder, _leaseDuration, _renewInterval, _logger);
    }

    /// <summary>
    ///     Creates the holder identity recorded on rows this instance acquires.
    /// </summary>
    /// <returns>The machine name plus the process id plus a per-instance GUID.</returns>
    /// <remarks>
    ///     The machine name and process id tell an operator which replica holds a lease; the GUID keeps two lease
    ///     instances in one process (tests, or a host that builds the composition twice) from renewing or releasing
    ///     each other's rows.
    /// </remarks>
    private static string CreateHolderIdentity()
    {
        return $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
    }

    /// <summary>
    ///     Holds one acquired lease row for the lifetime of the pass that acquired it, renewing it in the background.
    /// </summary>
    private sealed class LeaseHandle : IRecurringCommandLeaseHandle
    {
        /// <summary>
        ///     The PostgreSQL data source used to open one connection per lease statement.
        /// </summary>
        private readonly NpgsqlDataSource _dataSource;

        /// <summary>
        ///     The quoted qualified leases table name.
        /// </summary>
        private readonly string _tableName;

        /// <summary>
        ///     The lease name this handle holds.
        /// </summary>
        private readonly string _leaseName;

        /// <summary>
        ///     The holder identity recorded on the row.
        /// </summary>
        private readonly string _holder;

        /// <summary>
        ///     How long one acquisition holds the lease before it must be renewed.
        /// </summary>
        private readonly TimeSpan _leaseDuration;

        /// <summary>
        ///     How often the row is renewed while the handle is alive.
        /// </summary>
        private readonly TimeSpan _renewInterval;

        /// <summary>
        ///     Records renewal failures and stolen leases.
        /// </summary>
        private readonly ILogger<PostgreSqlRecurringCommandLease> _logger;

        /// <summary>
        ///     Stops the background renewal loop when the handle is disposed.
        /// </summary>
        private readonly CancellationTokenSource _renewalLifetime = new();

        /// <summary>
        ///     Signals the handle's loss token when a renewal finds the lease taken or fails.
        /// </summary>
        private readonly CancellationTokenSource _leaseLostLifetime = new();

        /// <summary>
        ///     The cached loss token handed out by <see cref="LeaseLost" />, still pollable after dispose.
        /// </summary>
        private readonly CancellationToken _leaseLost;

        /// <summary>
        ///     The background renewal loop, awaited on dispose so no renewal races the release delete.
        /// </summary>
        private readonly Task _renewal;

        /// <summary>
        ///     Whether the handle already released its row; disposing twice releases once.
        /// </summary>
        private int _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="LeaseHandle" /> class and starts its renewal loop.
        /// </summary>
        /// <param name="dataSource">The PostgreSQL data source used to open one connection per lease statement.</param>
        /// <param name="tableName">The quoted qualified leases table name.</param>
        /// <param name="leaseName">The lease name this handle holds.</param>
        /// <param name="holder">The holder identity recorded on the row.</param>
        /// <param name="leaseDuration">How long one acquisition holds the lease before it must be renewed.</param>
        /// <param name="renewInterval">How often the row is renewed while the handle is alive.</param>
        /// <param name="logger">Records renewal failures and stolen leases.</param>
        public LeaseHandle(
            NpgsqlDataSource dataSource,
            string tableName,
            string leaseName,
            string holder,
            TimeSpan leaseDuration,
            TimeSpan renewInterval,
            ILogger<PostgreSqlRecurringCommandLease> logger)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            ArgumentException.ThrowIfNullOrWhiteSpace(tableName);
            ArgumentException.ThrowIfNullOrWhiteSpace(leaseName);
            ArgumentException.ThrowIfNullOrWhiteSpace(holder);
            ArgumentNullException.ThrowIfNull(logger);

            _dataSource = dataSource;
            _tableName = tableName;
            _leaseName = leaseName;
            _holder = holder;
            _leaseDuration = leaseDuration;
            _renewInterval = renewInterval;
            _logger = logger;
            _leaseLost = _leaseLostLifetime.Token;
            _renewal = RenewLoopAsync(_renewalLifetime.Token);
        }

        /// <summary>
        ///     Gets a token that is cancelled when the lease is lost while the handle is held.
        /// </summary>
        /// <value>
        ///     A token that fires when another holder takes the lease or a renewal fails. A clean dispose never
        ///     cancels it; releasing the lease is not losing it.
        /// </value>
        public CancellationToken LeaseLost => _leaseLost;

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _renewalLifetime.Cancel();

            try
            {
                await _renewal.ConfigureAwait(false);
            }
            finally
            {
                _renewalLifetime.Dispose();
                _leaseLostLifetime.Dispose();
            }

            // The delete is ownership-checked, so a handle whose lease was taken (or whose row already expired and was
            // removed) releases nothing. A stolen handle must never delete the row out from under the new holder.
            var sql = $"""
                       DELETE FROM {_tableName}
                       WHERE lease_name = @leaseName AND holder = @holder;
                       """;

            using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
            using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("leaseName", _leaseName);
            command.Parameters.AddWithValue("holder", _holder);

            await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        /// <summary>
        ///     Renews the row until the handle is disposed, the renewal fails, or the lease is taken, signalling loss
        ///     on every stop but dispose.
        /// </summary>
        /// <param name="cancellationToken">A token that stops the loop when the handle is disposed.</param>
        /// <returns>A task that completes when the loop stops.</returns>
        private async Task RenewLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_renewInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                bool renewed;

                try
                {
                    renewed = await RenewOnceAsync().ConfigureAwait(false);
                }
#pragma warning disable CA1031 // Do not catch general exception types
                catch (Exception exception)
                {
                    // A renewal runs on a background loop with no caller to report to, and any failure mode (dropped
                    // table, dead database, cancelled pool) means this holder can no longer defend the row. Logging
                    // once and stopping lets the lease expire on the database clock so another instance takes over,
                    // instead of hammering a broken store once per interval for the life of the pass.
                    PostgreSqlRecurringLeaseLogMessages.LeaseRenewalFailed(_logger, _leaseName, exception);
                    SignalLeaseLost();
                    return;
                }
#pragma warning restore CA1031

                if (!renewed)
                {
                    PostgreSqlRecurringLeaseLogMessages.LeaseRenewalStolen(_logger, _leaseName, _holder);
                    SignalLeaseLost();
                    return;
                }
            }
        }

        /// <summary>
        ///     Cancels the handle's loss token so the pass holding the lease ends its busy period.
        /// </summary>
        private void SignalLeaseLost()
        {
            try
            {
                _leaseLostLifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The handle was disposed concurrently; the pass is already past its last loss check.
            }
        }

        /// <summary>
        ///     Pushes the row's expiry forward on the database clock when this holder still owns it.
        /// </summary>
        /// <returns>
        ///     <see langword="true" /> when the row still names this holder and was renewed; <see langword="false" />
        ///     when the lease was taken or the row is gone.
        /// </returns>
        /// <remarks>
        ///     The renewal is ownership-checked but not expiry-checked: an expired row nobody took is revived, which is
        ///     correct because no other holder owns it, while a row another holder took is left alone.
        /// </remarks>
        private async Task<bool> RenewOnceAsync()
        {
            var sql = $"""
                       UPDATE {_tableName}
                       SET expires_at = now() + @leaseDuration,
                           renewed_at = now()
                       WHERE lease_name = @leaseName AND holder = @holder;
                       """;

            using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
            using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("leaseName", _leaseName);
            command.Parameters.AddWithValue("holder", _holder);
            command.Parameters.AddWithValue("leaseDuration", _leaseDuration);

            return await command.ExecuteNonQueryAsync().ConfigureAwait(false) == 1;
        }
    }
}
