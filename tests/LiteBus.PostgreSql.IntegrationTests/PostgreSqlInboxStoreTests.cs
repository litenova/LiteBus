using LiteBus.Inbox.Abstractions;
using LiteBus.Inbox.PostgreSql;

namespace LiteBus.PostgreSql.IntegrationTests;

public sealed class PostgreSqlInboxStoreTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInboxStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AddAsync_ShouldReturnExistingCommandForDuplicateIdempotencyKey()
    {
        var options = CreateOptions();
        await PostgreSqlInboxSchema.CreateIfNotExistsAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var firstCommandId = Guid.NewGuid();
        var secondCommandId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var first = await store.AddAsync(new InboxCommandEnvelope
        {
            CommandId = firstCommandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxCommandStatus.Pending,
            IdempotencyKey = "ship-1"
        });

        var duplicate = await store.AddAsync(first with
        {
            CommandId = secondCommandId,
            Payload = "{\"orderId\":\"2\"}"
        });

        duplicate.CommandId.Should().Be(first.CommandId);
        duplicate.Payload.Should().Be(first.Payload);
    }

    [Fact]
    public async Task LeasePendingAsync_ShouldLeaseAndCompleteCommand()
    {
        var options = CreateOptions();
        await PostgreSqlInboxSchema.CreateIfNotExistsAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var commandId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await store.AddAsync(new InboxCommandEnvelope
        {
            CommandId = commandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxCommandStatus.Pending
        });

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();
        leased[0].CommandId.Should().Be(commandId);
        leased[0].Status.Should().Be(InboxCommandStatus.Processing);
        leased[0].AttemptCount.Should().Be(1);
        leased[0].LeaseOwner.Should().Be("worker-1");

        await store.MarkCompletedAsync(commandId);

        var afterCompletion = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        afterCompletion.Should().BeEmpty();
    }

    private static PostgreSqlInboxStoreOptions CreateOptions()
    {
        return new PostgreSqlInboxStoreOptions
        {
            SchemaName = "litebus_tests",
            TableName = $"inbox_{Guid.NewGuid():N}"
        };
    }
}