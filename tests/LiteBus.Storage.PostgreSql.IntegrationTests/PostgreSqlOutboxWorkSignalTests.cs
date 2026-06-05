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
    ///     Confirms a broken listener connection is replaced on the next wait cycle.
    /// </summary>
    [Fact]
    public async Task WaitForWorkOrDelayAsync_after_listener_breaks_should_open_new_connection()
    {
        await using var signal = new PostgreSqlOutboxWorkSignal(_fixture.DataSource);

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

    private static Task<NpgsqlConnection?> GetListenerConnectionAsync(PostgreSqlOutboxWorkSignal signal)
    {
        var field = typeof(PostgreSqlOutboxWorkSignal).GetField(
            "_listenerConnection",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return Task.FromResult(field?.GetValue(signal) as NpgsqlConnection);
    }
}
