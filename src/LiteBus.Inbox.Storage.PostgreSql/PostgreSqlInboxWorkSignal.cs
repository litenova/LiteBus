using System;
using System.Threading;
using System.Threading.Tasks;
using LiteBus.Inbox.Abstractions;
using LiteBus.Storage.PostgreSql.Stores;
using Npgsql;

namespace LiteBus.Inbox.Storage.PostgreSql;

/// <summary>
///     Listens for PostgreSQL <c>NOTIFY</c> events on the inbox work channel and falls back to polling delays.
/// </summary>
public sealed class PostgreSqlInboxWorkSignal : IInboxWorkSignal, IAsyncDisposable
{
    /// <summary>
    ///     The shared PostgreSQL listener used by this adapter.
    /// </summary>
    private readonly PostgreSqlWorkSignal _inner;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlInboxWorkSignal" /> class.
    /// </summary>
    /// <param name="dataSource">The PostgreSQL data source.</param>
    public PostgreSqlInboxWorkSignal(NpgsqlDataSource dataSource)
    {
        _inner = new PostgreSqlWorkSignal(dataSource, PostgreSqlInboxNotifyChannel.ChannelName);
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