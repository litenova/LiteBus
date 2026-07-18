using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Outbox.Abstractions;
using LiteBus.Storage.PostgreSql.Stores;
using Npgsql;

namespace LiteBus.Outbox.Storage.PostgreSql;

/// <summary>
///     Listens for PostgreSQL <c>NOTIFY</c> events on the outbox work channel and falls back to polling delays.
/// </summary>
public sealed class PostgreSqlOutboxWorkSignal : IOutboxWorkSignal, IAsyncDisposable
{
    /// <summary>
    ///     The shared PostgreSQL listener used by this adapter.
    /// </summary>
    private readonly PostgreSqlWorkSignal _inner;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlOutboxWorkSignal" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    public PostgreSqlOutboxWorkSignal(NpgsqlDataSource dataSource)
    {
        _inner = new PostgreSqlWorkSignal(dataSource, PostgreSqlOutboxNotifyChannel.ChannelName);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        return _inner.DisposeAsync();
    }

    /// <inheritdoc />
    public Task WaitForWorkOrDelayAsync(TimeSpan pollInterval, CancellationToken cancellationToken = default)
    {
        return _inner.WaitForWorkOrDelayAsync(pollInterval, cancellationToken);
    }
}