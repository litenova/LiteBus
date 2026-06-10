using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.Storage.PostgreSql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Integration tests proving PostgreSQL <c>NOTIFY</c> wakes inbox work signals without polling.
/// </summary>
public sealed class PostgreSqlInboxWorkSignalNotifyTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInboxWorkSignalNotifyTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Confirms an insert notification wakes a waiting work signal before the poll timeout elapses.
    /// </summary>
    [Fact]
    public async Task WaitForWorkOrDelayAsync_when_row_inserted_should_wake_before_poll_timeout()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);
        var store = new PostgreSqlInboxStore(_fixture.DataSource, options);

        await using var signal = new PostgreSqlInboxWorkSignal(_fixture.DataSource);

        var waitTask = signal.WaitForWorkOrDelayAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(250));

        await store.AddAsync(new InboxEnvelope
        {
            Id = Guid.NewGuid(),
            ContractName = "tests.commands.notify",
            ContractVersion = 1,
            Payload = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
            AttemptCount = 0,
            Status = InboxStatus.Pending
        });

        var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(5))) == waitTask;
        completed.Should().BeTrue("the insert notify trigger should wake the listener before the poll timeout");

        await waitTask;
    }
}
