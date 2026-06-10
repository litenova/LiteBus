using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql.Stores;
using Npgsql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Integration tests for <see cref="PostgreSqlWorkSignal" /> listener reconnection behavior.
/// </summary>
public sealed class PostgreSqlInboxWorkSignalTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInboxWorkSignalTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms a server-terminated listener connection is replaced after the reconnect delay.
    /// </summary>
    [Fact]
    public async Task WaitForWorkOrDelayAsync_after_listener_breaks_should_open_new_connection()
    {
        await using var signal = new PostgreSqlWorkSignal(
            _fixture.DataSource,
            PostgreSqlInboxNotifyChannel.ChannelName);

        await signal.WaitForWorkOrDelayAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        var firstConnection = GetListenerConnection(signal);
        firstConnection.Should().NotBeNull();
        firstConnection!.State.Should().Be(System.Data.ConnectionState.Open);

        await TerminateBackendAsync(firstConnection.ProcessID).ConfigureAwait(false);

        NpgsqlConnection? secondConnection = null;
        var reconnected = await PostgreSqlTestInfrastructure.WaitUntilAsync(
            () =>
            {
                secondConnection = GetListenerConnection(signal);
                return Task.FromResult(secondConnection is not null
                    && !ReferenceEquals(firstConnection, secondConnection)
                    && secondConnection.State == System.Data.ConnectionState.Open);
            },
            TimeSpan.FromSeconds(5));

        reconnected.Should().BeTrue("the work signal should replace a terminated listener connection");
        secondConnection.Should().NotBeNull();
        secondConnection!.State.Should().Be(System.Data.ConnectionState.Open);
    }

    private async Task TerminateBackendAsync(int backendProcessId)
    {
        await using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT pg_terminate_backend(@pid)";
        command.Parameters.AddWithValue("pid", backendProcessId);
        await command.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static NpgsqlConnection? GetListenerConnection(PostgreSqlWorkSignal signal)
    {
        var field = typeof(PostgreSqlWorkSignal).GetField(
            "_listenerConnection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return field?.GetValue(signal) as NpgsqlConnection;
    }
}
