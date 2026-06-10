using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using Npgsql;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Listens for PostgreSQL <c>NOTIFY</c> events on the inbox work channel and falls back to polling delays.
/// </summary>
public sealed class PostgreSqlInboxWorkSignal : IInboxWorkSignal, IAsyncDisposable
{
    /// <summary>
    ///     The delay applied before reconnecting a broken listener connection.
    /// </summary>
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Signals that a notification arrived or the polling timeout elapsed.
    /// </summary>
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

    /// <summary>
    ///     Gets the PostgreSQL data source used to open the dedicated listener connection.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     Serializes listener startup and loop creation.
    /// </summary>
    private readonly SemaphoreSlim _listenerGate = new(1, 1);

    /// <summary>
    ///     Serializes listener loop startup.
    /// </summary>
    private readonly object _listenerLoopSync = new();

    /// <summary>
    ///     Gets the dedicated listener connection, if one has been opened.
    /// </summary>
    private NpgsqlConnection? _listenerConnection;

    /// <summary>
    ///     Cancels the background <see cref="NpgsqlConnection.WaitAsync" /> loop.
    /// </summary>
    private CancellationTokenSource? _listenerLoopCts;

    /// <summary>
    ///     The background task that blocks on <see cref="NpgsqlConnection.WaitAsync" /> until notifications arrive.
    /// </summary>
    private Task? _listenerLoopTask;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxWorkSignal" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    public PostgreSqlInboxWorkSignal(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource ?? throw new ArgumentNullException(nameof(dataSource));
    }

    /// <inheritdoc />
    public async Task WaitForWorkOrDelayAsync(TimeSpan pollInterval, CancellationToken cancellationToken = default)
    {
        EnsureListenerLoopStarted();
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
        CancellationTokenSource? listenerLoopCts;
        Task? listenerLoopTask;

        lock (_listenerLoopSync)
        {
            listenerLoopCts = _listenerLoopCts;
            listenerLoopTask = _listenerLoopTask;
            _listenerLoopCts = null;
            _listenerLoopTask = null;
        }

        if (listenerLoopCts is not null)
        {
            await listenerLoopCts.CancelAsync().ConfigureAwait(false);
        }

        if (listenerLoopTask is not null)
        {
            try
            {
                await listenerLoopTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected when the listener loop is cancelled during disposal.
            }
        }

        listenerLoopCts?.Dispose();

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
    ///     Starts the dedicated background loop that calls <see cref="NpgsqlConnection.WaitAsync" />.
    /// </summary>
    private void EnsureListenerLoopStarted()
    {
        lock (_listenerLoopSync)
        {
            if (_listenerLoopTask is not null)
            {
                return;
            }

            _listenerLoopCts = new CancellationTokenSource();
            _listenerLoopTask = RunListenerLoopAsync(_listenerLoopCts.Token);
        }
    }

    /// <summary>
    ///     Blocks on <see cref="NpgsqlConnection.WaitAsync" /> and reconnects when the listener connection breaks.
    /// </summary>
    /// <param name="cancellationToken">A token that stops the background loop.</param>
    /// <returns>A task that represents the listener loop.</returns>
    private async Task RunListenerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await EnsureListenerStartedAsync(cancellationToken).ConfigureAwait(false);

                var connection = _listenerConnection;
                if (connection is null)
                {
                    continue;
                }

                await connection.WaitAsync(cancellationToken).ConfigureAwait(false);
                ReleaseSignal();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                InvalidateListenerConnection();

                try
                {
                    await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
        }
    }

    /// <summary>
    ///     Opens the listener connection and subscribes to inbox work notifications.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel listener startup.</param>
    /// <returns>A task that completes when the listener is ready.</returns>
    private async Task EnsureListenerStartedAsync(CancellationToken cancellationToken)
    {
        if (_listenerConnection is { State: ConnectionState.Open })
        {
            return;
        }

        await _listenerGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_listenerConnection is { State: ConnectionState.Open })
            {
                return;
            }

            if (_listenerConnection is not null)
            {
                DetachListenerConnection(_listenerConnection);
                await _listenerConnection.DisposeAsync().ConfigureAwait(false);
                _listenerConnection = null;
            }

            var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            connection.Notification += OnNotification;
            connection.StateChange += OnListenerConnectionStateChange;
            await using var command = connection.CreateCommand();
            command.CommandText = $"LISTEN {PostgreSqlInboxNotifyChannel.ChannelName}";
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            _listenerConnection = connection;
        }
        finally
        {
            _listenerGate.Release();
        }
    }

    /// <summary>
    ///     Releases the work signal when PostgreSQL delivers an inbox notification.
    /// </summary>
    /// <param name="sender">The connection that raised the event.</param>
    /// <param name="e">The notification payload.</param>
    private void OnNotification(object? sender, NpgsqlNotificationEventArgs e)
    {
        ReleaseSignal();
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
        ReleaseSignal();
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

    /// <summary>
    ///     Releases one waiter without throwing when the semaphore has already been disposed.
    /// </summary>
    private void ReleaseSignal()
    {
        try
        {
            _signal.Release();
        }
        catch (ObjectDisposedException)
        {
            // The work signal is shutting down.
        }
    }
}
