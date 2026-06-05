using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using Npgsql;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Listens for PostgreSQL <c>NOTIFY</c> events on the outbox work channel and falls back to polling delays.
/// </summary>
public sealed class PostgreSqlOutboxWorkSignal : IOutboxWorkSignal, IAsyncDisposable
{
    /// <summary>
    ///     Signals that a notification arrived or the polling timeout elapsed.
    /// </summary>
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

    /// <summary>
    ///     Gets the PostgreSQL data source used to open the dedicated listener connection.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     Serializes listener startup.
    /// </summary>
    private readonly SemaphoreSlim _listenerGate = new(1, 1);

    /// <summary>
    ///     Gets the dedicated listener connection, if one has been opened.
    /// </summary>
    private NpgsqlConnection? _listenerConnection;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxWorkSignal" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    public PostgreSqlOutboxWorkSignal(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    /// <inheritdoc />
    public async Task WaitForWorkOrDelayAsync(TimeSpan pollInterval, CancellationToken cancellationToken = default)
    {
        await EnsureListenerStartedAsync(cancellationToken).ConfigureAwait(false);

        if (pollInterval <= TimeSpan.Zero)
        {
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await _signal.WaitAsync(pollInterval, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_listenerConnection is not null)
        {
            await _listenerConnection.DisposeAsync().ConfigureAwait(false);
            _listenerConnection = null;
        }

        _signal.Dispose();
        _listenerGate.Dispose();
    }

    /// <summary>
    ///     Opens the listener connection and subscribes to outbox work notifications.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel listener startup.</param>
    /// <returns>A task that completes when the listener is ready.</returns>
    private async Task EnsureListenerStartedAsync(CancellationToken cancellationToken)
    {
        if (_listenerConnection is not null)
        {
            return;
        }

        await _listenerGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_listenerConnection is not null)
            {
                return;
            }

            var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            connection.Notification += (_, _) => _signal.Release();
            await using var command = connection.CreateCommand();
            command.CommandText = $"LISTEN {PostgreSqlOutboxNotifyChannel.ChannelName}";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _listenerConnection = connection;
        }
        finally
        {
            _listenerGate.Release();
        }
    }
}
