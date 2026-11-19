using System.Diagnostics.CodeAnalysis;
using LiteBus.Auditing.Storage.PostgreSql;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Storage.PostgreSql;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies the PostgreSQL audit trail: success records commit and roll back with the business transaction,
///     refusal and failure records persist out of band, mapped fields round-trip, and a blank segment fails.
/// </summary>
public sealed class PostgreSqlAuditTrailTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditTrailTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlAuditTrailTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task WriteAsync_ForSuccess_ShouldCommitWithTheBusinessTransaction()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var provider = new TestTransactionProvider();
        var trail = CreateTrail(options, provider);
        var action = $"orders.place-{Guid.NewGuid():N}";

        using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        provider.Activate(connection, transaction);
        await trail.WriteAsync(SuccessRecord(action)).ConfigureAwait(false);
        provider.Deactivate();
        await transaction.CommitAsync().ConfigureAwait(false);

        var stored = await FindUnlinkedAsync(options, action).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Outcome.Should().Be(AuditOutcome.Succeeded);
        stored.Action.Should().Be(action);
    }

    [SkippableFact]
    public async Task WriteAsync_ForSuccess_WhenBusinessTransactionRollsBack_ShouldLoseTheRecord()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var provider = new TestTransactionProvider();
        var trail = CreateTrail(options, provider);
        var action = $"orders.place-{Guid.NewGuid():N}";

        using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        provider.Activate(connection, transaction);
        await trail.WriteAsync(SuccessRecord(action)).ConfigureAwait(false);
        provider.Deactivate();
        await transaction.RollbackAsync().ConfigureAwait(false);

        var stored = await FindUnlinkedAsync(options, action).ConfigureAwait(false);
        stored.Should().BeNull();
    }

    [SkippableFact]
    public async Task WriteAsync_ForFailure_ShouldPersistOutOfBand()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var provider = new TestTransactionProvider();
        var trail = CreateTrail(options, provider);
        var action = $"orders.place-{Guid.NewGuid():N}";

        using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        provider.Activate(connection, transaction);
        await trail.WriteAsync(FailureRecord(action)).ConfigureAwait(false);
        provider.Deactivate();
        await transaction.RollbackAsync().ConfigureAwait(false);

        var stored = await FindUnlinkedAsync(options, action).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Outcome.Should().Be(AuditOutcome.Failed);
    }

    [SkippableFact]
    public async Task WriteAsync_ForDenied_ShouldPersistOutOfBand()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var provider = new TestTransactionProvider();
        var trail = CreateTrail(options, provider);
        var action = $"orders.place-{Guid.NewGuid():N}";

        using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        provider.Activate(connection, transaction);
        await trail.WriteAsync(DeniedRecord(action)).ConfigureAwait(false);
        provider.Deactivate();
        await transaction.RollbackAsync().ConfigureAwait(false);

        var stored = await FindUnlinkedAsync(options, action).ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Outcome.Should().Be(AuditOutcome.Denied);
    }

    [SkippableFact]
    public async Task WriteAsync_ShouldMapRecordFieldsWithRecordIdentity()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var trail = CreateTrail(options, null);
        var identity = Guid.NewGuid();
        var action = $"orders.place-{Guid.NewGuid():N}";

        var record = new AuditRecord
        {
            Id = identity,
            Action = action,
            Actor = new AuditActor { Id = "account-7", Kind = AuditActor.UserKind },
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = PostgreSqlTestInfrastructure.BaseTime,
            Duration = TimeSpan.FromMilliseconds(12),
            Category = "orders",
            TargetKind = "order",
            TargetId = "order-9",
            Reason = "customer request",
            MessageType = "PlaceOrderCommand",
            CorrelationId = "correlation-3",
            TenantId = "tenant-a",
            Properties = new Dictionary<string, string>(StringComparer.Ordinal) { ["channel"] = "web" }
        };

        await trail.WriteAsync(record).ConfigureAwait(false);

        var stored = await FindUnlinkedAsync(options, action, "orders").ConfigureAwait(false);
        stored.Should().NotBeNull();
        stored!.Id.Should().Be(identity);
        stored.Action.Should().Be(action);
        stored.ActorId.Should().Be("account-7");
        stored.ActorKind.Should().Be(AuditActor.UserKind);
        stored.OccurredAt.Should().Be(PostgreSqlTestInfrastructure.BaseTime);
        stored.Duration.Should().Be(TimeSpan.FromMilliseconds(12));
        stored.Category.Should().Be("orders");
        stored.TargetKind.Should().Be("order");
        stored.TargetId.Should().Be("order-9");
        stored.Reason.Should().Be("customer request");
        stored.MessageType.Should().Be("PlaceOrderCommand");
        stored.CorrelationId.Should().Be("correlation-3");
        stored.TenantId.Should().Be("tenant-a");
        stored.Properties.Should().Contain("channel", "web");
        stored.Segment.Should().Be("orders/2026");
        stored.Sequence.Should().Be(0);
    }

    [SkippableFact]
    public async Task WriteAsync_WhenSegmenterReturnsBlank_ShouldThrow()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var trail = new PostgreSqlAuditTrail<PostgreSqlAuditRecord>(
            new PostgreSqlAuditChainStore<PostgreSqlAuditRecord>(
                new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options)),
            new PostgreSqlAuditRecordMapper(),
            new BlankSegmenter());

        var write = async () =>
        {
            await trail.WriteAsync(SuccessRecord("orders.place-order")).ConfigureAwait(false);
        };

        await write.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    private static AuditRecord SuccessRecord(string action)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = PostgreSqlTestInfrastructure.BaseTime
        };
    }

    private static AuditRecord FailureRecord(string action)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Failed,
            OccurredAt = PostgreSqlTestInfrastructure.BaseTime,
            FailureCode = "OutOfStockException"
        };
    }

    private static AuditRecord DeniedRecord(string action)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = AuditOutcome.Denied,
            OccurredAt = PostgreSqlTestInfrastructure.BaseTime
        };
    }

    private PostgreSqlAuditTrail<PostgreSqlAuditRecord> CreateTrail(
        PostgreSqlAuditStoreOptions options,
        IPostgreSqlTransactionProvider? provider)
    {
        return new PostgreSqlAuditTrail<PostgreSqlAuditRecord>(
            new PostgreSqlAuditChainStore<PostgreSqlAuditRecord>(
                new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options),
                provider),
            new PostgreSqlAuditRecordMapper(),
            new PostgreSqlAuditRecordSegmenter());
    }

    private async Task<PostgreSqlAuditRecord?> FindUnlinkedAsync(
        PostgreSqlAuditStoreOptions options,
        string action,
        string? category = null)
    {
        var store = new PostgreSqlAuditChainStore<PostgreSqlAuditRecord>(
            new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options));
        var segmenter = new PostgreSqlAuditRecordSegmenter();
        var probe = new PostgreSqlAuditRecord
        {
            Action = action,
            Category = category,
            OccurredAt = PostgreSqlTestInfrastructure.BaseTime
        };
        var segment = segmenter.SegmentOf(probe);

        await foreach (var document in store.ListUnlinkedAsync(segment, null, CancellationToken.None).ConfigureAwait(false))
        {
            if (string.Equals(document.Action, action, StringComparison.Ordinal))
            {
                return document;
            }
        }

        return null;
    }

    private static PostgreSqlAuditStoreOptions CreateOptions()
    {
        var suffix = Guid.NewGuid().ToString("N");

        return new PostgreSqlAuditStoreOptions
        {
            SchemaName = PostgreSqlTestInfrastructure.TestSchemaName,
            TableName = $"audit_records_{suffix}",
            HeadsTableName = $"audit_heads_{suffix}",
            CheckpointsTableName = $"audit_checkpoints_{suffix}"
        };
    }

    private sealed class TestTransactionProvider : IPostgreSqlTransactionProvider
    {
        private NpgsqlConnection? _connection;
        private NpgsqlTransaction? _transaction;

        public void Activate(NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            _connection = connection;
            _transaction = transaction;
        }

        public void Deactivate()
        {
            _connection = null;
            _transaction = null;
        }

        public bool TryGetCurrent(
            [NotNullWhen(true)] out NpgsqlConnection? connection,
            [NotNullWhen(true)] out NpgsqlTransaction? transaction)
        {
            connection = _connection;
            transaction = _transaction;

            return connection is not null && transaction is not null;
        }
    }

    private sealed class BlankSegmenter : IAuditSegmenter<PostgreSqlAuditRecord>
    {
        public string SegmentOf(PostgreSqlAuditRecord document)
        {
            return "  ";
        }

        public PostgreSqlAuditRecord? CreateGenesis(string segment, PostgreSqlAuditRecord first)
        {
            return null;
        }
    }
}
