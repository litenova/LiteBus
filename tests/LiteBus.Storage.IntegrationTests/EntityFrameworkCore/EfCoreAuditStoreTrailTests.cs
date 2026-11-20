using LiteBus.Auditing.Storage.EntityFrameworkCore;
using LiteBus.Auditing.Abstractions;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Integration tests for the Entity Framework Core audit trail: successes join the business context,
///     refusals and failures persist out of band, and records map onto stored documents.
/// </summary>
public sealed class EfCoreAuditStoreTrailTests : IClassFixture<SqliteDatabaseFixture>
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly SqliteDatabaseFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreAuditStoreTrailTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreAuditStoreTrailTests(SqliteDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Success_record_commits_with_the_business_context()
    {
        var action = $"orders.place-order-{Guid.NewGuid():N}";
        var category = $"trail-success-{Guid.NewGuid():N}";

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var trail = CreateTrail(business);

            await trail.WriteAsync(SuccessRecord(action, category)).ConfigureAwait(false);

            var beforeCommit = await FindAuditsAsync(action).ConfigureAwait(false);
            beforeCommit.Should().BeEmpty();

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var stored = await FindAuditsAsync(action).ConfigureAwait(false);
        var record = stored.Should().ContainSingle().Subject;
        record.Action.Should().Be(action);
        record.Outcome.Should().Be(AuditOutcome.Succeeded);
        record.Sequence.Should().Be(0);
        record.Segment.Should().Be($"{category}/2026");
    }

    [Fact]
    public async Task Success_record_rolls_back_with_the_business_context()
    {
        var action = $"orders.place-order-{Guid.NewGuid():N}";
        var category = $"trail-rollback-{Guid.NewGuid():N}";

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var trail = CreateTrail(business);

            await trail.WriteAsync(SuccessRecord(action, category)).ConfigureAwait(false);
        }

        var stored = await FindAuditsAsync(action).ConfigureAwait(false);
        stored.Should().BeEmpty();
    }

    [Fact]
    public async Task Failure_denial_cancellation_and_invalid_records_persist_without_the_business_commit()
    {
        var failedAction = $"orders.charge-card-{Guid.NewGuid():N}";
        var deniedAction = $"orders.refund-order-{Guid.NewGuid():N}";
        var cancelledAction = $"orders.cancel-order-{Guid.NewGuid():N}";
        var invalidAction = $"orders.place-order-{Guid.NewGuid():N}";
        var category = $"trail-failure-{Guid.NewGuid():N}";

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var trail = CreateTrail(business);

            await trail.WriteAsync(FailedRecord(failedAction, category), CancellationToken.None).ConfigureAwait(false);
            await trail.WriteAsync(DeniedRecord(deniedAction, category), CancellationToken.None).ConfigureAwait(false);
            await trail.WriteAsync(CancelledRecord(cancelledAction, category), CancellationToken.None).ConfigureAwait(false);
            await trail.WriteAsync(InvalidRecord(invalidAction, category), CancellationToken.None).ConfigureAwait(false);
        }

        var failed = await FindAuditsAsync(failedAction).ConfigureAwait(false);
        failed.Should().ContainSingle().Which.Outcome.Should().Be(AuditOutcome.Failed);

        var denied = await FindAuditsAsync(deniedAction).ConfigureAwait(false);
        denied.Should().ContainSingle().Which.Outcome.Should().Be(AuditOutcome.Denied);

        var cancelled = await FindAuditsAsync(cancelledAction).ConfigureAwait(false);
        cancelled.Should().ContainSingle().Which.Outcome.Should().Be(AuditOutcome.Canceled);

        var invalid = await FindAuditsAsync(invalidAction).ConfigureAwait(false);
        invalid.Should().ContainSingle().Which.Outcome.Should().Be(AuditOutcome.Invalid);
    }

    [Fact]
    public async Task Records_map_fields_and_keep_the_record_identity()
    {
        var action = $"orders.place-order-{Guid.NewGuid():N}";
        var record = new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Actor = AuditActor.User("account-7"),
            Outcome = AuditOutcome.Failed,
            OccurredAt = OccurredAt,
            Duration = TimeSpan.FromMilliseconds(12),
            Category = $"trail-mapping-{Guid.NewGuid():N}",
            TargetKind = "order",
            TargetId = "order-9",
            Reason = "customer request",
            FailureCode = "E_ORDER_FAILED",
            MessageType = "PlaceOrder",
            CorrelationId = "correlation-1",
            TenantId = "tenant-a",
            Properties = new Dictionary<string, string> { ["order-total"] = "9.50" }
        };

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var trail = CreateTrail(business);

            await trail.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);
        }

        var stored = await FindAuditsAsync(action).ConfigureAwait(false);
        var document = stored.Should().ContainSingle().Subject;
        document.Id.Should().Be(record.Id);
        document.ActorId.Should().Be("account-7");
        document.ActorKind.Should().Be(AuditActor.UserKind);
        document.TargetKind.Should().Be("order");
        document.TargetId.Should().Be("order-9");
        document.Reason.Should().Be("customer request");
        document.FailureCode.Should().Be("E_ORDER_FAILED");
        document.MessageType.Should().Be("PlaceOrder");
        document.CorrelationId.Should().Be("correlation-1");
        document.TenantId.Should().Be("tenant-a");
        document.Properties.Should().Contain("order-total", "9.50");
    }

    [Fact]
    public async Task Failure_record_without_a_factory_throws_a_configuration_error()
    {
        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var trail = new EfCoreAuditTrail<EfCoreAuditRecord>(
                new EfCoreAuditChainStore<EfCoreAuditRecord>(business),
                new EfCoreAuditRecordMapper(),
                new EfCoreAuditRecordSegmenter());

            var write = () => trail.WriteAsync(
                FailedRecord($"orders.charge-card-{Guid.NewGuid():N}", "orders"),
                CancellationToken.None);

            var thrown = await write.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage("*IAuditDbContextFactory*");
        }
    }

    [Fact]
    public async Task Blank_segment_throws()
    {
        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var store = new EfCoreAuditChainStore<EfCoreAuditRecord>(business, null, new TestAuditDbContextFactory(_fixture.ConnectionString));
            var trail = new EfCoreAuditTrail<EfCoreAuditRecord>(
                store,
                new EfCoreAuditRecordMapper(),
                new FuncSegmenter(_ => "  "));

            var write = () => trail.WriteAsync(FailedRecord("orders.place-order", "orders"), CancellationToken.None);

            var thrown = await write.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage("*segment*");
        }
    }

    private EfCoreAuditTrail<EfCoreAuditRecord> CreateTrail(EfCoreAuditTestDbContext business)
    {
        return new EfCoreAuditTrail<EfCoreAuditRecord>(
            new EfCoreAuditChainStore<EfCoreAuditRecord>(business, null, new TestAuditDbContextFactory(_fixture.ConnectionString)),
            new EfCoreAuditRecordMapper(),
            new EfCoreAuditRecordSegmenter());
    }

    private async Task<EfCoreAuditTestDbContext> CreatePreparedContextAsync()
    {
        var context = new EfCoreAuditTestDbContext(
            new DbContextOptionsBuilder<EfCoreAuditTestDbContext>()
                .UseSqlite(_fixture.ConnectionString)
                .ReplaceService<IModelCacheKeyFactory, EfCoreAuditTestDbContext.ModelCacheKeyFactory>()
                .Options,
            new EntityFrameworkCoreAuditStoreOptions(),
            EfCoreStorageProvider.Sqlite);

        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        return context;
    }

    private async Task<IReadOnlyList<EfCoreAuditRecord>> FindAuditsAsync(string action)
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await context.Set<EfCoreAuditRecord>()
                .Where(record => record.Action == action)
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }

    private static AuditRecord SuccessRecord(string action, string category)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = OccurredAt,
            Category = category
        };
    }

    private static AuditRecord FailedRecord(string action, string category)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Failed,
            OccurredAt = OccurredAt,
            Category = category,
            FailureCode = "E_ORDER_FAILED"
        };
    }

    private static AuditRecord DeniedRecord(string action, string category)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Denied,
            OccurredAt = OccurredAt,
            Category = category,
            Reason = "the caller lacks the orders.refund permission"
        };
    }

    private static AuditRecord CancelledRecord(string action, string category)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Canceled,
            OccurredAt = OccurredAt,
            Category = category
        };
    }

    private static AuditRecord InvalidRecord(string action, string category)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Invalid,
            OccurredAt = OccurredAt,
            Category = category
        };
    }

    private sealed class TestAuditDbContextFactory : IAuditDbContextFactory
    {
        private readonly string _connectionString;

        public TestAuditDbContextFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        public IAuditDbContext CreateDbContext()
        {
            var context = new EfCoreAuditTestDbContext(
                new DbContextOptionsBuilder<EfCoreAuditTestDbContext>()
                    .UseSqlite(_connectionString)
                    .ReplaceService<IModelCacheKeyFactory, EfCoreAuditTestDbContext.ModelCacheKeyFactory>()
                    .Options,
                new EntityFrameworkCoreAuditStoreOptions(),
                EfCoreStorageProvider.Sqlite);

            context.Database.EnsureCreated();

            return context;
        }
    }

    private sealed class FuncSegmenter : IAuditSegmenter<EfCoreAuditRecord>
    {
        private readonly Func<EfCoreAuditRecord, string> _segment;

        public FuncSegmenter(Func<EfCoreAuditRecord, string> segment)
        {
            _segment = segment;
        }

        public string SegmentOf(EfCoreAuditRecord document)
        {
            return _segment(document);
        }

        public EfCoreAuditRecord? CreateGenesis(string segment, EfCoreAuditRecord first)
        {
            return null;
        }
    }
}
