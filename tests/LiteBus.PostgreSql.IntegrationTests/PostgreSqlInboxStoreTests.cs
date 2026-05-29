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
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var firstCommandId = Guid.NewGuid();
        var secondCommandId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

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
        (await PostgreSqlTableReaders.CountInboxRowsAsync(_fixture.DataSource, options)).Should().Be(1);
    }

    [Fact]
    public async Task AddAsync_WhenCommandIdAlreadyExists_ShouldReturnExistingRow()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var commandId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        var first = await store.AddAsync(CreatePendingEnvelope(commandId, now) with { IdempotencyKey = null });
        var duplicate = await store.AddAsync(first with { Payload = "{\"changed\":true}" });

        duplicate.CommandId.Should().Be(commandId);
        duplicate.Payload.Should().Be(first.Payload);
    }

    [Fact]
    public async Task AddAsync_ShouldPersistMetadataAndVisibleAfter()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var commandId = Guid.NewGuid();
        var visibleAfter = PostgreSqlTestInfrastructure.BaseTime.AddHours(2);

        await store.AddAsync(new InboxCommandEnvelope
        {
            CommandId = commandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = PostgreSqlTestInfrastructure.BaseTime,
            VisibleAfter = visibleAfter,
            AttemptCount = 0,
            Status = InboxCommandStatus.Pending,
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            TenantId = "tenant-1"
        });

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, commandId);
        row.Should().NotBeNull();
        row!.VisibleAfter.Should().Be(visibleAfter);
        row.CorrelationId.Should().Be("correlation-1");
        row.CausationId.Should().Be("causation-1");
        row.TenantId.Should().Be("tenant-1");
    }

    [Fact]
    public async Task LeasePendingAsync_ShouldLeaseAndCompleteCommand()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var commandId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        await store.AddAsync(CreatePendingEnvelope(commandId, now));

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

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, commandId);
        row!.Status.Should().Be(InboxCommandStatus.Completed);
        row.LeaseOwner.Should().BeNull();
        row.LastError.Should().BeNull();
    }

    [Fact]
    public async Task LeasePendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseCommand()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var commandId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        await store.AddAsync(CreatePendingEnvelope(commandId, now) with
        {
            VisibleAfter = now.AddHours(1)
        });

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-1",
            Now = now.AddMinutes(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().BeEmpty();
    }

    [Fact]
    public async Task LeasePendingAsync_ShouldOrderByCreatedAtAndRespectBatchSize()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var now = PostgreSqlTestInfrastructure.BaseTime;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        await store.AddAsync(CreatePendingEnvelope(firstId, now));
        await store.AddAsync(CreatePendingEnvelope(secondId, now.AddSeconds(1)));
        await store.AddAsync(CreatePendingEnvelope(thirdId, now.AddSeconds(2)));

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 2,
            LeaseOwner = "worker-1",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        });

        leased.Should().HaveCount(2);
        leased[0].CommandId.Should().Be(firstId);
        leased[1].CommandId.Should().Be(secondId);
    }

    [Fact]
    public async Task MarkFailedAsync_ShouldSetFailedStateAndVisibleAfter()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var commandId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;
        var visibleAfter = now.AddMinutes(10);

        await store.AddAsync(CreatePendingEnvelope(commandId, now));

        await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.MarkFailedAsync(new InboxCommandFailure
        {
            CommandId = commandId,
            Error = "transient failure",
            VisibleAfter = visibleAfter
        });

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, commandId);
        row!.Status.Should().Be(InboxCommandStatus.Failed);
        row.VisibleAfter.Should().Be(visibleAfter);
        row.LastError.Should().Be("transient failure");
        row.LeaseOwner.Should().BeNull();
    }

    [Fact]
    public async Task LeasePendingAsync_WhenFailedAndVisibleAfterReached_ShouldLeaseAgain()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var commandId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        await store.AddAsync(CreatePendingEnvelope(commandId, now));

        await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.MarkFailedAsync(new InboxCommandFailure
        {
            CommandId = commandId,
            Error = "retry me",
            VisibleAfter = now.AddMinutes(5)
        });

        var hidden = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        hidden.Should().BeEmpty();

        var visible = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(6),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        visible.Should().ContainSingle();
        visible[0].AttemptCount.Should().Be(2);
        visible[0].Status.Should().Be(InboxCommandStatus.Processing);
    }

    [Fact]
    public async Task MoveToDeadLetterAsync_ShouldSetDeadLetteredStatus()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var commandId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        await store.AddAsync(CreatePendingEnvelope(commandId, now));

        await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await store.MoveToDeadLetterAsync(new InboxCommandDeadLetter
        {
            CommandId = commandId,
            Reason = "exhausted retries"
        });

        var row = await PostgreSqlTableReaders.ReadInboxAsync(_fixture.DataSource, options, commandId);
        row!.Status.Should().Be(InboxCommandStatus.DeadLettered);
        row.LastError.Should().Be("exhausted retries");
        row.LeaseOwner.Should().BeNull();

        var leased = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-2",
            Now = now.AddHours(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().BeEmpty();
    }

    [Fact]
    public async Task LeasePendingAsync_WhenLeaseExpires_ShouldReclaimProcessingCommand()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var commandId = Guid.NewGuid();
        var now = PostgreSqlTestInfrastructure.BaseTime;

        await store.AddAsync(CreatePendingEnvelope(commandId, now));

        await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-worker",
            Now = now,
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        var reclaimed = await store.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "fresh-worker",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        reclaimed.Should().ContainSingle();
        reclaimed[0].CommandId.Should().Be(commandId);
        reclaimed[0].LeaseOwner.Should().Be("fresh-worker");
        reclaimed[0].AttemptCount.Should().Be(2);
    }

    [Fact]
    public async Task LeasePendingAsync_ConcurrentWorkers_ShouldLeaseDisjointCommands()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions();
        await PostgreSqlTestInfrastructure.EnsureInboxSchemaAsync(_fixture.DataSource, options);

        var store = new PostgreSqlCommandInboxStore(_fixture.DataSource, options);
        var now = PostgreSqlTestInfrastructure.BaseTime;

        for (var index = 0; index < 8; index++)
        {
            await store.AddAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(index)));
        }

        var request = new InboxLeaseRequest
        {
            BatchSize = 4,
            LeaseOwner = "worker",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        };

        var firstBatchTask = store.LeasePendingAsync(request with { LeaseOwner = "worker-a" });
        var secondBatchTask = store.LeasePendingAsync(request with { LeaseOwner = "worker-b" });
        await Task.WhenAll(firstBatchTask, secondBatchTask);
        var firstBatch = await firstBatchTask;
        var secondBatch = await secondBatchTask;

        var leasedIds = firstBatch.Select(command => command.CommandId)
            .Concat(secondBatch.Select(command => command.CommandId))
            .ToArray();

        leasedIds.Should().HaveCount(8);
        leasedIds.Should().OnlyHaveUniqueItems();
        firstBatch.Should().OnlyContain(command => command.LeaseOwner == "worker-a");
        secondBatch.Should().OnlyContain(command => command.LeaseOwner == "worker-b");
    }

    private static InboxCommandEnvelope CreatePendingEnvelope(Guid commandId, DateTimeOffset createdAt)
    {
        return new InboxCommandEnvelope
        {
            CommandId = commandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = InboxCommandStatus.Pending,
            IdempotencyKey = $"ship:{commandId:N}"
        };
    }
}
