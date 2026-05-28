using LiteBus.Outbox.Abstractions;
using LiteBus.Outbox.PostgreSql;

namespace LiteBus.PostgreSql.IntegrationTests;

public sealed class PostgreSqlOutboxStoreTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task LeasePendingAsync_ShouldRespectRetryVisibility()
    {
        var options = CreateOptions();
        await PostgreSqlOutboxSchema.CreateIfNotExistsAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await store.AddAsync(new OutboxMessageEnvelope
        {
            MessageId = messageId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            Topic = "orders",
            CreatedAt = now,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0
        });

        var leased = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();
        leased[0].MessageId.Should().Be(messageId);
        leased[0].Status.Should().Be(OutboxMessageStatus.Publishing);
        leased[0].AttemptCount.Should().Be(1);

        await store.MarkFailedAsync(new OutboxMessageFailure
        {
            MessageId = messageId,
            Error = "publisher unavailable",
            VisibleAfter = now.AddMinutes(5)
        });

        var hidden = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        hidden.Should().BeEmpty();

        var visible = await store.LeasePendingAsync(new OutboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "publisher-2",
            Now = now.AddMinutes(6),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        visible.Should().ContainSingle();
        visible[0].AttemptCount.Should().Be(2);

        await store.MarkPublishedAsync(messageId);
    }

    [Fact]
    public async Task AddAsync_ShouldReturnExistingMessageWhenMessageIdAlreadyExists()
    {
        var options = CreateOptions();
        await PostgreSqlOutboxSchema.CreateIfNotExistsAsync(_fixture.DataSource, options);

        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);
        var messageId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        var first = await store.AddAsync(new OutboxMessageEnvelope
        {
            MessageId = messageId,
            ContractName = "tests.events.submitted",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = now,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0
        });

        var duplicate = await store.AddAsync(first with
        {
            Payload = "{\"orderId\":\"2\"}"
        });

        duplicate.MessageId.Should().Be(first.MessageId);
        duplicate.Payload.Should().Be(first.Payload);
    }

    private static PostgreSqlOutboxStoreOptions CreateOptions()
    {
        return new PostgreSqlOutboxStoreOptions
        {
            SchemaName = "litebus_tests",
            TableName = $"outbox_{Guid.NewGuid():N}"
        };
    }
}