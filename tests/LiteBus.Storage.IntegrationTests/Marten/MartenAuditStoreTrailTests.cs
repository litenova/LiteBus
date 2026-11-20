using System.Data;
using LiteBus.Auditing.Storage.Marten;
using LiteBus.Auditing.Abstractions;
using LiteBus.Storage.IntegrationTests.PostgreSql;
using Marten;

namespace LiteBus.Storage.IntegrationTests.Marten;

/// <summary>
///     Integration tests for the Marten audit trail: successes join the business session, refusals and failures
///     persist out of band, and records map onto stored documents.
/// </summary>
public sealed class MartenAuditStoreTrailTests : IClassFixture<PostgreSqlFixture>
{
    private static readonly DateTimeOffset OccurredAt = new(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="MartenAuditStoreTrailTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public MartenAuditStoreTrailTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task Success_record_commits_with_the_business_session()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var action = $"orders.place-order-{Guid.NewGuid():N}";
        var category = $"trail-success-{Guid.NewGuid():N}";

        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var trail = CreateTrail(documentStore, business);

            await trail.WriteAsync(SuccessRecord(action, category)).ConfigureAwait(false);

            var beforeCommit = await FindAuditsAsync(documentStore, action).ConfigureAwait(false);
            beforeCommit.Should().BeEmpty();

            await business.SaveChangesAsync().ConfigureAwait(false);
        }

        var stored = await FindAuditsAsync(documentStore, action).ConfigureAwait(false);
        var record = stored.Should().ContainSingle().Subject;
        record.Action.Should().Be(action);
        record.Outcome.Should().Be(AuditOutcome.Succeeded);
        record.Sequence.Should().Be(0);
        record.Segment.Should().Be($"{category}/2026");
    }

    [SkippableFact]
    public async Task Success_record_rolls_back_with_the_business_session()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var action = $"orders.place-order-{Guid.NewGuid():N}";
        var category = $"trail-rollback-{Guid.NewGuid():N}";

        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var trail = CreateTrail(documentStore, business);

            await trail.WriteAsync(SuccessRecord(action, category)).ConfigureAwait(false);
        }

        var stored = await FindAuditsAsync(documentStore, action).ConfigureAwait(false);
        stored.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task Failure_and_denial_records_persist_without_the_business_commit()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var failedAction = $"orders.charge-card-{Guid.NewGuid():N}";
        var deniedAction = $"orders.refund-order-{Guid.NewGuid():N}";
        var category = $"trail-failure-{Guid.NewGuid():N}";

        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var trail = CreateTrail(documentStore, business);

            await trail.WriteAsync(FailedRecord(failedAction, category), CancellationToken.None).ConfigureAwait(false);
            await trail.WriteAsync(DeniedRecord(deniedAction, category), CancellationToken.None).ConfigureAwait(false);
        }

        var failed = await FindAuditsAsync(documentStore, failedAction).ConfigureAwait(false);
        failed.Should().ContainSingle().Which.Outcome.Should().Be(AuditOutcome.Failed);

        var denied = await FindAuditsAsync(documentStore, deniedAction).ConfigureAwait(false);
        denied.Should().ContainSingle().Which.Outcome.Should().Be(AuditOutcome.Denied);
    }

    [SkippableFact]
    public async Task Records_map_fields_and_keep_the_record_identity()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
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

        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var trail = CreateTrail(documentStore, business);

            await trail.WriteAsync(record, CancellationToken.None).ConfigureAwait(false);
        }

        var stored = await FindAuditsAsync(documentStore, action).ConfigureAwait(false);
        var document = stored.Should().ContainSingle().Subject;
        document.Id.Should().Be(record.Id);
        document.ActorId.Should().Be("account-7");
        document.TargetKind.Should().Be("order");
        document.TargetId.Should().Be("order-9");
        document.Reason.Should().Be("customer request");
        document.FailureCode.Should().Be("E_ORDER_FAILED");
        document.MessageType.Should().Be("PlaceOrder");
        document.CorrelationId.Should().Be("correlation-1");
        document.TenantId.Should().Be("tenant-a");
        document.Properties.Should().Contain("order-total", "9.50");
    }

    [SkippableFact]
    public async Task Blank_segment_throws()
    {
        var documentStore = CreateDocumentStore();
        await using var configuredDocumentStore = documentStore.ConfigureAwait(false);
        var business = documentStore.LightweightSession(IsolationLevel.ReadCommitted);
        await using (business.ConfigureAwait(false))
        {
            var store = new MartenAuditChainStore<MartenAuditRecord>(documentStore, business);
            var trail = new MartenAuditTrail<MartenAuditRecord>(
                store,
                new MartenAuditRecordMapper(),
                new MartenAuditFuncSegmenter<MartenAuditRecord>(_ => "  "));

            var write = () => trail.WriteAsync(FailedRecord("orders.place-order", "orders"), CancellationToken.None);

            var thrown = await write.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
            thrown.WithMessage("*segment*");
        }
    }

    private static MartenAuditTrail<MartenAuditRecord> CreateTrail(IDocumentStore documentStore, IDocumentSession session)
    {
        return new MartenAuditTrail<MartenAuditRecord>(
            new MartenAuditChainStore<MartenAuditRecord>(documentStore, session),
            new MartenAuditRecordMapper(),
            new MartenAuditRecordSegmenter());
    }

    private DocumentStore CreateDocumentStore()
    {
        return DocumentStore.For(options =>
        {
            options.Connection(_fixture.ConnectionString);
            options.DatabaseSchemaName = "marten_auditing_store_tests";
        });
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

    private static async Task<IReadOnlyList<MartenAuditRecord>> FindAuditsAsync(
        IDocumentStore documentStore,
        string action)
    {
        var session = documentStore.QuerySession();
        await using (session.ConfigureAwait(false))
        {
            return await session
                .Query<MartenAuditRecord>()
                .Where(record => record.Action == action)
                .ToListAsync(CancellationToken.None)
                .ConfigureAwait(false);
        }
    }
}
