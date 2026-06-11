using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.Storage.PostgreSql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Integration tests proving PostgreSQL <c>NOTIFY</c> wakes outbox work signals without polling.
/// </summary>
public sealed class PostgreSqlOutboxWorkSignalNotifyTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxWorkSignalNotifyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms an insert notification wakes a waiting work signal before the poll timeout elapses.
    /// </summary>
    [Fact]
    public async Task WaitForWorkOrDelayAsync_when_row_inserted_should_wake_before_poll_timeout()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options);
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);

        await using var signal = new PostgreSqlOutboxWorkSignal(_fixture.DataSource);

        var waitTask = signal.WaitForWorkOrDelayAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(250));

        await store.AddAsync(new OutboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.events.notify",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = OutboxStatus.Pending
        });

        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5))) == waitTask;
        completed.Should().BeTrue("the insert notify trigger should wake the listener before the poll timeout");

        await waitTask;
    }
}