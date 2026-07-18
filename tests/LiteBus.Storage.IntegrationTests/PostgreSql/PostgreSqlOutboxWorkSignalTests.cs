using System.Data;
using System.Reflection;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql.Stores;
using Npgsql;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Integration tests for <see cref="PostgreSqlWorkSignal" /> listener reconnection behavior.
/// </summary>
public sealed class PostgreSqlOutboxWorkSignalTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxWorkSignalTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms a server-terminated listener connection is replaced after the reconnect delay.
    /// </summary>
    [Fact]
    public async Task WaitForWorkOrDelayAsync_after_listener_breaks_should_open_new_connection()
    {
         var signal = new PostgreSqlWorkSignal(             _fixture.DataSource,             PostgreSqlOutboxNotifyChannel.ChannelName);
         await using (signal.ConfigureAwait(true))
         {

        await signal.WaitForWorkOrDelayAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None).ConfigureAwait(true);

        var firstConnection = GetListenerConnection(signal);
        firstConnection.Should().NotBeNull();
        firstConnection!.State.Should().Be(ConnectionState.Open);

        await TerminateBackendAsync(firstConnection.ProcessID).ConfigureAwait(true);

        NpgsqlConnection? secondConnection = null;

        var reconnected = await PostgreSqlTestInfrastructure.WaitUntilAsync(
            () =>
            {
                secondConnection = GetListenerConnection(signal);
                return Task.FromResult(secondConnection is not null && !ReferenceEquals(firstConnection, secondConnection) && secondConnection.State == ConnectionState.Open);
            },
            TimeSpan.FromSeconds(5));

        reconnected.Should().BeTrue("the work signal should replace a terminated listener connection");
        secondConnection.Should().NotBeNull();
        secondConnection!.State.Should().Be(ConnectionState.Open);
        }
    }

    private async Task TerminateBackendAsync(int backendProcessId)
    {
         var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
         await using (connection.ConfigureAwait(false))
         {
         var command = connection.CreateCommand();
         await using (command.ConfigureAwait(false))
         {
        command.CommandText = "SELECT pg_terminate_backend(@pid)";
        command.Parameters.AddWithValue("pid", backendProcessId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
        }
    }

    private static NpgsqlConnection? GetListenerConnection(PostgreSqlWorkSignal signal)
    {
        var field = typeof(PostgreSqlWorkSignal).GetField(
            "_listenerConnection",
            BindingFlags.Instance | BindingFlags.NonPublic);

        return field?.GetValue(signal) as NpgsqlConnection;
    }
}
