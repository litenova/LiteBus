using System;
using System.Data;
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
        var connection = _listenerConnection;
        _listenerConnection = null;

        if (connection is not null)
        {
            DetachListenerConnection(connection);
            await connection.DisposeAsync().ConfigureAwait(false);
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
            connection.Notification += OnNotification;
            connection.StateChange += OnListenerConnectionStateChange;
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

    /// <summary>
    ///     Releases the work signal when PostgreSQL delivers an outbox notification.
    /// </summary>
    /// <param name="sender">The connection that raised the event.</param>
    /// <param name="e">The notification payload.</param>
    private void OnNotification(object? sender, NpgsqlNotificationEventArgs e)
    {
        _signal.Release();
    }

    /// <summary>
    ///     Clears the listener reference and wakes waiters when the dedicated connection closes or breaks.
    /// </summary>
    /// <param name="sender">The connection that raised the event.</param>
    /// <param name="e">The connection state transition details.</param>
    private void OnListenerConnectionStateChange(object? sender, StateChangeEventArgs e)
    {
        if (e.CurrentState is not (ConnectionState.Closed or ConnectionState.Broken))
        {
            return;
        }

        if (ReferenceEquals(sender, _listenerConnection))
        {
            InvalidateListenerConnection();
        }
    }

    /// <summary>
    ///     Detaches event handlers and clears the listener so the next wait cycle can reconnect.
    /// </summary>
    private void InvalidateListenerConnection()
    {
        var connection = _listenerConnection;
        if (connection is null)
        {
            return;
        }

        _listenerConnection = null;
        DetachListenerConnection(connection);
        _signal.Release();
    }

    /// <summary>
    ///     Unsubscribes notification and state handlers from a listener connection.
    /// </summary>
    /// <param name="connection">The listener connection to detach.</param>
    private void DetachListenerConnection(NpgsqlConnection connection)
    {
        connection.Notification -= OnNotification;
        connection.StateChange -= OnListenerConnectionStateChange;
    }
}
