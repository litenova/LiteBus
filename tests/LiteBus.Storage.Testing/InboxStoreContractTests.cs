using LiteBus.Inbox.Abstractions;

namespace LiteBus.Storage.Testing;

/// <summary>
///     Shared inbox store contract tests for in-memory, PostgreSQL, and EF Core implementations.
/// </summary>
public abstract class InboxStoreContractTests
{
    /// <summary>
    ///     Gets a fixed UTC timestamp used as the baseline for lease and visibility assertions.
    /// </summary>
    protected static DateTimeOffset BaseTime { get; } = new(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     Creates a fresh store instance for one test.
    /// </summary>
    /// <returns>The writer, lease, and state roles backed by the same store instance.</returns>
    protected abstract InboxStoreRoles CreateStore();

    /// <summary>
    ///     Holds the three inbox store roles implemented by one persistence backend.
    /// </summary>
    /// <param name="Writer">The append-only writer role.</param>
    /// <param name="LeaseStore">The lease role used by the processor.</param>
    /// <param name="StateStore">The execution result role used by the processor.</param>
    public sealed record InboxStoreRoles(
        IInboxStore Writer,
        IInboxLeaseStore LeaseStore,
        IInboxStateStore StateStore);

    /// <summary>
    ///     Verifies that duplicate idempotency keys return the original stored command.
    /// </summary>
    [Fact]
    public async Task AddAsync_ShouldReturnExistingCommandForDuplicateIdempotencyKey()
    {
        var roles = CreateStore();
        var firstCommandId = Guid.NewGuid();
        var secondCommandId = Guid.NewGuid();
        var now = BaseTime;

        var first = await roles.Writer.AddAsync(new InboxEnvelope
        {
            Id = firstCommandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = now,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = "ship-1"
        });

        var duplicate = await roles.Writer.AddAsync(first with
        {
            Id = secondCommandId,
            Payload = "{\"orderId\":\"2\"}"
        });

        duplicate.Id.Should().Be(first.Id);
        duplicate.Payload.Should().Be(first.Payload);
    }

    /// <summary>
    ///     Verifies that duplicate command identifiers return the original stored row.
    /// </summary>
    [Fact]
    public async Task AddAsync_WhenCommandIdAlreadyExists_ShouldReturnExistingRow()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        var first = await roles.Writer.AddAsync(CreatePendingEnvelope(commandId, now) with { IdempotencyKey = null });
        var duplicate = await roles.Writer.AddAsync(first with { Payload = "{\"changed\":true}" });

        duplicate.Id.Should().Be(commandId);
        duplicate.Payload.Should().Be(first.Payload);
    }

    /// <summary>
    ///     Verifies that trace metadata and delayed visibility are preserved on append.
    /// </summary>
    [Fact]
    public async Task AddAsync_ShouldPersistMetadataAndVisibleAfter()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var visibleAfter = BaseTime.AddHours(2);

        var stored = await roles.Writer.AddAsync(new InboxEnvelope
        {
            Id = commandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = BaseTime,
            VisibleAfter = visibleAfter,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            CorrelationId = "correlation-1",
            CausationId = "causation-1",
            TenantId = "tenant-1"
        });

        stored.VisibleAfter.Should().Be(visibleAfter);
        stored.CorrelationId.Should().Be("correlation-1");
        stored.CausationId.Should().Be("causation-1");
        stored.TenantId.Should().Be("tenant-1");
    }

    /// <summary>
    ///     Verifies that leasing, completion, and re-leasing behave as expected.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ShouldLeaseAndCompleteCommand()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.AddAsync(CreatePendingEnvelope(commandId, now));

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-1",
            Now = now.AddSeconds(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().ContainSingle();
        leased[0].Id.Should().Be(commandId);
        leased[0].Status.Should().Be(InboxStatus.Processing);
        leased[0].AttemptCount.Should().Be(1);
        leased[0].LeaseOwner.Should().Be("worker-1");

        await roles.StateStore.MarkCompletedAsync(commandId);

        var afterCompletion = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(2),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        afterCompletion.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that commands with a future visible-after timestamp are not leased early.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenVisibleAfterInFuture_ShouldNotLeaseCommand()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.AddAsync(CreatePendingEnvelope(commandId, now) with
        {
            VisibleAfter = now.AddHours(1)
        });

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-1",
            Now = now.AddMinutes(30),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that leasing orders by created time and respects the batch size.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ShouldOrderByCreatedAtAndRespectBatchSize()
    {
        var roles = CreateStore();
        var now = BaseTime;
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();

        await roles.Writer.AddAsync(CreatePendingEnvelope(firstId, now));
        await roles.Writer.AddAsync(CreatePendingEnvelope(secondId, now.AddSeconds(1)));
        await roles.Writer.AddAsync(CreatePendingEnvelope(thirdId, now.AddSeconds(2)));

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 2,
            LeaseOwner = "worker-1",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        });

        leased.Should().HaveCount(2);
        leased[0].Id.Should().Be(firstId);
        leased[1].Id.Should().Be(secondId);
    }

    /// <summary>
    ///     Verifies that mark-failed records retry visibility and diagnostic text.
    /// </summary>
    [Fact]
    public async Task MarkFailedAsync_ShouldSetFailedStateAndVisibleAfter()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;
        var visibleAfter = now.AddMinutes(10);

        await roles.Writer.AddAsync(CreatePendingEnvelope(commandId, now));

        await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateStore.MarkFailedAsync(new InboxEnvelopeFailure
        {
            Id = commandId,
            Error = "transient failure",
            VisibleAfter = visibleAfter
        });

        var hidden = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        hidden.Should().BeEmpty();

        var visible = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = visibleAfter,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        visible.Should().ContainSingle();
        visible[0].Status.Should().Be(InboxStatus.Processing);
        visible[0].LastError.Should().Be("transient failure");
    }

    /// <summary>
    ///     Verifies that failed commands become leasable again once visible-after is reached.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenFailedAndVisibleAfterReached_ShouldLeaseAgain()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.AddAsync(CreatePendingEnvelope(commandId, now));

        await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateStore.MarkFailedAsync(new InboxEnvelopeFailure
        {
            Id = commandId,
            Error = "retry me",
            VisibleAfter = now.AddMinutes(5)
        });

        var hidden = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        hidden.Should().BeEmpty();

        var visible = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-2",
            Now = now.AddMinutes(6),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        visible.Should().ContainSingle();
        visible[0].AttemptCount.Should().Be(2);
        visible[0].Status.Should().Be(InboxStatus.Processing);
    }

    /// <summary>
    ///     Verifies that dead-lettered commands are not leased again.
    /// </summary>
    [Fact]
    public async Task MoveToDeadLetterAsync_ShouldSetDeadLetteredStatus()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.AddAsync(CreatePendingEnvelope(commandId, now));

        await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "worker-1",
            Now = now,
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        await roles.StateStore.MoveToDeadLetterAsync(new InboxEnvelopeDeadLetter
        {
            Id = commandId,
            Reason = "exhausted retries"
        });

        var leased = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 5,
            LeaseOwner = "worker-2",
            Now = now.AddHours(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        leased.Should().BeEmpty();
    }

    /// <summary>
    ///     Verifies that expired processing leases can be reclaimed by another worker.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_WhenLeaseExpires_ShouldReclaimProcessingCommand()
    {
        var roles = CreateStore();
        var commandId = Guid.NewGuid();
        var now = BaseTime;

        await roles.Writer.AddAsync(CreatePendingEnvelope(commandId, now));

        await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "stale-worker",
            Now = now,
            LeaseDuration = TimeSpan.FromSeconds(30)
        });

        var reclaimed = await roles.LeaseStore.LeasePendingAsync(new InboxLeaseRequest
        {
            BatchSize = 1,
            LeaseOwner = "fresh-worker",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(1)
        });

        reclaimed.Should().ContainSingle();
        reclaimed[0].Id.Should().Be(commandId);
        reclaimed[0].LeaseOwner.Should().Be("fresh-worker");
        reclaimed[0].AttemptCount.Should().Be(2);
    }

    /// <summary>
    ///     Verifies that concurrent lease calls claim disjoint commands when the store supports it.
    /// </summary>
    [Fact]
    public async Task LeasePendingAsync_ConcurrentWorkers_ShouldLeaseDisjointCommands()
    {
        var roles = CreateStore();
        var now = BaseTime;

        for (var index = 0; index < 8; index++)
        {
            await roles.Writer.AddAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(index)));
        }

        var request = new InboxLeaseRequest
        {
            BatchSize = 4,
            LeaseOwner = "worker",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        };

        var firstBatchTask = roles.LeaseStore.LeasePendingAsync(request with { LeaseOwner = "worker-a" });
        var secondBatchTask = roles.LeaseStore.LeasePendingAsync(request with { LeaseOwner = "worker-b" });
        await Task.WhenAll(firstBatchTask, secondBatchTask);
        var firstBatch = await firstBatchTask;
        var secondBatch = await secondBatchTask;

        var leasedIds = firstBatch.Select(command => command.Id)
            .Concat(secondBatch.Select(command => command.Id))
            .ToArray();

        leasedIds.Should().HaveCount(8);
        leasedIds.Should().OnlyHaveUniqueItems();
        firstBatch.Should().OnlyContain(command => command.LeaseOwner == "worker-a");
        secondBatch.Should().OnlyContain(command => command.LeaseOwner == "worker-b");
    }

    /// <summary>
    ///     Creates a pending envelope for contract tests.
    /// </summary>
    /// <param name="commandId">The command identifier.</param>
    /// <param name="createdAt">The storage timestamp.</param>
    /// <returns>A pending envelope ready for append.</returns>
    protected static InboxEnvelope CreatePendingEnvelope(Guid commandId, DateTimeOffset createdAt)
    {
        return new InboxEnvelope
        {
            Id = commandId,
            ContractName = "tests.commands.ship",
            ContractVersion = 1,
            Payload = "{\"orderId\":\"1\"}",
            CreatedAt = createdAt,
            AttemptCount = 0,
            Status = InboxStatus.Pending,
            IdempotencyKey = $"ship:{commandId:N}"
        };
    }
}
