using LiteBus.Inbox.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Integration tests for <see cref="PostgreSqlInboxWorkSignal" /> listener reconnection behavior.
/// </summary>
public sealed class PostgreSqlInboxWorkSignalTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInboxWorkSignalTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms a broken listener connection is replaced on the next wait cycle.
    /// </summary>
    [Fact]
    public async Task WaitForWorkOrDelayAsync_after_listener_breaks_should_open_new_connection()
    {
        await using var signal = new PostgreSqlInboxWorkSignal(_fixture.DataSource);

        await signal.WaitForWorkOrDelayAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        var firstConnection = await GetListenerConnectionAsync(signal);
        firstConnection.Should().NotBeNull();
        firstConnection!.State.Should().Be(System.Data.ConnectionState.Open);

        await firstConnection.CloseAsync();

        await signal.WaitForWorkOrDelayAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None);

        var secondConnection = await GetListenerConnectionAsync(signal);
        secondConnection.Should().NotBeNull();
        ReferenceEquals(firstConnection, secondConnection).Should().BeFalse();
        secondConnection!.State.Should().Be(System.Data.ConnectionState.Open);
    }

    private static Task<NpgsqlConnection?> GetListenerConnectionAsync(PostgreSqlInboxWorkSignal signal)
    {
        var field = typeof(PostgreSqlInboxWorkSignal).GetField(
            "_listenerConnection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return Task.FromResult(field?.GetValue(signal) as NpgsqlConnection);
    }
}
