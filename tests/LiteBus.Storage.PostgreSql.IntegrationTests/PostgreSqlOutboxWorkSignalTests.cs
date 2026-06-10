using LiteBus.Outbox.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Integration tests for <see cref="PostgreSqlOutboxWorkSignal" /> listener reconnection behavior.
/// </summary>
public sealed class PostgreSqlOutboxWorkSignalTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxWorkSignalTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms a broken listener connection is replaced after the reconnect delay.
    /// </summary>
    [Trait("Category", "Quarantine")]
    [Fact(Skip = "Npgsql rejects CloseAsync while the LISTEN connection is blocked in Waiting state.")]
    public async Task WaitForWorkOrDelayAsync_after_listener_breaks_should_open_new_connection()
    {
        await using var signal = new PostgreSqlOutboxWorkSignal(_fixture.DataSource);

        await signal.WaitForWorkOrDelayAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        var firstConnection = await GetListenerConnectionAsync(signal);
        firstConnection.Should().NotBeNull();
        firstConnection!.State.Should().Be(System.Data.ConnectionState.Open);

        await firstConnection.CloseAsync();

        NpgsqlConnection? secondConnection = null;
        var reconnected = await PostgreSqlTestInfrastructure.WaitUntilAsync(
            async () =>
            {
                secondConnection = await GetListenerConnectionAsync(signal);
                return secondConnection is not null
                    && !ReferenceEquals(firstConnection, secondConnection)
                    && secondConnection.State == System.Data.ConnectionState.Open;
            },
            TimeSpan.FromSeconds(5));

        reconnected.Should().BeTrue("the work signal should replace a closed listener connection");
        secondConnection.Should().NotBeNull();
        secondConnection!.State.Should().Be(System.Data.ConnectionState.Open);
    }

    private static Task<NpgsqlConnection?> GetListenerConnectionAsync(PostgreSqlOutboxWorkSignal signal)
    {
        var field = typeof(PostgreSqlOutboxWorkSignal).GetField(
            "_listenerConnection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return Task.FromResult(field?.GetValue(signal) as NpgsqlConnection);
    }
}
