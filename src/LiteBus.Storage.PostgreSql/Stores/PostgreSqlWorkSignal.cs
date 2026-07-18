using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Npgsql;

namespace LiteBus.Storage.PostgreSql.Stores;

/// <summary>
///     Listens for PostgreSQL <c>NOTIFY</c> events on a work channel and falls back to polling delays.
/// </summary>
public sealed class PostgreSqlWorkSignal : IAsyncDisposable
{
    /// <summary>
    ///     The delay applied before reconnecting a broken listener connection.
    /// </summary>
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(1);

    /// <summary>
    ///     Gets the <c>LISTEN</c> channel subscribed to by this work signal.
    /// </summary>
    private readonly string _channelName;

    /// <summary>
    ///     Gets the PostgreSQL data source used to open the dedicated listener connection.
    /// </summary>
    private readonly NpgsqlDataSource _dataSource;

    /// <summary>
    ///     Serializes listener startup and loop creation.
    /// </summary>
    private readonly SemaphoreSlim _listenerGate = new(1, 1);

    /// <summary>
    ///     Serializes listener connection publication and invalidation across callbacks and reconnect attempts.
    /// </summary>
    private readonly object _listenerConnectionSync = new();

    /// <summary>
    ///     Serializes listener loop startup.
    /// </summary>
    private readonly object _listenerLoopSync = new();

    /// <summary>
    ///     Signals that a notification arrived or the polling timeout elapsed.
    /// </summary>
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

    /// <summary>
    ///     Gets the dedicated listener connection, if one has been opened.
    /// </summary>
    private NpgsqlConnection? _listenerConnection;

    /// <summary>
    ///     Cancels the background <c>WaitAsync</c> listener loop.
    /// </summary>
    private CancellationTokenSource? _listenerLoopCts;

    /// <summary>
    ///     The background task that blocks on <c>WaitAsync</c> until notifications arrive.
    /// </summary>
    private Task? _listenerLoopTask;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlWorkSignal" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    /// <param name="channelName">The notification channel name used by insert triggers.</param>
    public PostgreSqlWorkSignal(NpgsqlDataSource dataSource, string channelName)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        _dataSource = dataSource;
        ArgumentException.ThrowIfNullOrWhiteSpace(channelName);
        _channelName = channelName;
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

        var connection = TakeListenerConnection();

        if (connection is not null)
        {
            DetachListenerConnection(connection);
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        _signal.Dispose();
        _listenerGate.Dispose();
    }

    /// <summary>
    ///     Waits until work arrives on the notification channel or the polling interval elapses.
    /// </summary>
    /// <param name="pollInterval">The maximum delay before returning when no notification arrives.</param>
    /// <param name="cancellationToken">A token that cancels the wait.</param>
    /// <returns>A task that completes when work is signaled or the poll interval expires.</returns>
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

    /// <summary>
    ///     Starts the dedicated background loop that calls <c>WaitAsync</c>.
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
    ///     Blocks on <c>WaitAsync</c> and reconnects when the listener connection breaks.
    /// </summary>
    /// <remarks>
    ///     The listener loop catches <see cref="Exception" /> after broker-specific failures because
    ///     notification wait can surface BCL exceptions that are not typed as <see cref="NpgsqlException" />.
    /// </remarks>
    /// <param name="cancellationToken">A token that stops the background loop.</param>
    /// <returns>A task that represents the listener loop.</returns>
    private async Task RunListenerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            NpgsqlConnection? connection = null;

            try
            {
                await EnsureListenerStartedAsync(cancellationToken).ConfigureAwait(false);

                connection = GetListenerConnection();

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
            catch (NpgsqlException)
            {
                InvalidateListenerConnection(connection);

                try
                {
                    await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
#pragma warning disable CA1031 // Last-resort boundary: listener failures can surface as BCL exceptions outside NpgsqlException.
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                InvalidateListenerConnection(connection);

                try
                {
                    await Task.Delay(ReconnectDelay, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
            }
#pragma warning restore CA1031
        }
    }

    /// <summary>
    ///     Opens the listener connection and subscribes to work notifications.
    /// </summary>
    /// <param name="cancellationToken">A token used to cancel listener startup.</param>
    /// <returns>A task that completes when the listener is ready.</returns>
    private async Task EnsureListenerStartedAsync(CancellationToken cancellationToken)
    {
        if (GetListenerConnection() is { State: ConnectionState.Open })
        {
            return;
        }

        await _listenerGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var existingConnection = GetListenerConnection();

            if (existingConnection is { State: ConnectionState.Open })
            {
                return;
            }

            existingConnection = TakeListenerConnection(existingConnection);

            if (existingConnection is not null)
            {
                DetachListenerConnection(existingConnection);
                await existingConnection.DisposeAsync().ConfigureAwait(false);
            }

            var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            connection.Notification += OnNotification;
            connection.StateChange += OnListenerConnectionStateChange;

            try
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"LISTEN {_channelName}";
                await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                SetListenerConnection(connection);
            }
            catch
            {
                DetachListenerConnection(connection);
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _listenerGate.Release();
        }
    }

    /// <summary>
    ///     Releases the work signal when PostgreSQL delivers a notification.
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

        if (sender is NpgsqlConnection connection)
        {
            InvalidateListenerConnection(connection);
        }
    }

    /// <summary>
    ///     Clears and disposes the expected listener connection, then wakes waiters so a replacement can start.
    /// </summary>
    /// <param name="expectedConnection">The connection whose fault triggered invalidation.</param>
    private void InvalidateListenerConnection(NpgsqlConnection? expectedConnection)
    {
        if (expectedConnection is null)
        {
            return;
        }

        var connection = TakeListenerConnection(expectedConnection);

        if (connection is null)
        {
            return;
        }

        DetachListenerConnection(connection);

        try
        {
            connection.Dispose();
        }
        finally
        {
            ReleaseSignal();
        }
    }

    /// <summary>
    ///     Gets the currently published listener connection.
    /// </summary>
    /// <returns>The active or reconnecting listener connection, if one is published.</returns>
    private NpgsqlConnection? GetListenerConnection()
    {
        lock (_listenerConnectionSync)
        {
            return _listenerConnection;
        }
    }

    /// <summary>
    ///     Publishes a listener connection after its <c>LISTEN</c> command succeeds.
    /// </summary>
    /// <param name="connection">The subscribed connection to publish.</param>
    private void SetListenerConnection(NpgsqlConnection connection)
    {
        lock (_listenerConnectionSync)
        {
            _listenerConnection = connection;
        }
    }

    /// <summary>
    ///     Removes the published listener connection when it matches the expected instance.
    /// </summary>
    /// <param name="expectedConnection">
    ///     The connection expected to be published, or <see langword="null" /> to remove any published connection.
    /// </param>
    /// <returns>The removed connection, or <see langword="null" /> when the expected instance is no longer current.</returns>
    private NpgsqlConnection? TakeListenerConnection(NpgsqlConnection? expectedConnection = null)
    {
        lock (_listenerConnectionSync)
        {
            if (_listenerConnection is null ||
                expectedConnection is not null && !ReferenceEquals(_listenerConnection, expectedConnection))
            {
                return null;
            }

            var connection = _listenerConnection;
            _listenerConnection = null;
            return connection;
        }
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
