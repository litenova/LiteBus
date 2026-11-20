using System.Diagnostics.CodeAnalysis;
using LiteBus.Auditing.Storage.PostgreSql;
using LiteBus.Auditing.Abstractions;
using LiteBus.Storage.PostgreSql;
using LiteBus.Storage.Testing;
using Npgsql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies the PostgreSQL audit chain store: the shared chain port contract plus ambient transaction staging,
///     out-of-band staging, existing-connection binding, latest-checkpoint reads, and append rejection paths.
/// </summary>
public sealed class PostgreSqlAuditChainStoreTests
    : AuditChainStoreContractTests<PostgreSqlAuditRecord>, IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlAuditChainStoreTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlAuditChainStoreTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <inheritdoc />
    protected override IAuditChainStore<PostgreSqlAuditRecord> CreateStore()
    {
        var options = CreateOptions();
        PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).GetAwaiter().GetResult();

        return new PostgreSqlAuditChainStore<PostgreSqlAuditRecord>(
            new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options));
    }

    /// <inheritdoc />
    protected override PostgreSqlAuditRecord CreateDocument(string marker)
    {
        return new PostgreSqlAuditRecord
        {
            Id = Guid.NewGuid(),
            Action = marker,
            OccurredAt = BaseTime
        };
    }

    /// <inheritdoc />
    protected override string GetMarker(PostgreSqlAuditRecord document)
    {
        return document.Action;
    }

    [SkippableFact]
    public async Task StageAsync_WithActiveAmbientTransaction_ShouldCommitAndRollBackWithIt()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var provider = new TestTransactionProvider();

        var store = new PostgreSqlAuditChainStore<PostgreSqlAuditRecord>(
            new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options),
            provider);

        using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);

        using (var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false))
        {
            provider.Activate(connection, transaction);
            await store.StageAsync("orders", CreateDocument("rolled-back"), CancellationToken.None).ConfigureAwait(false);
            provider.Deactivate();
            await transaction.RollbackAsync().ConfigureAwait(false);
        }

        var afterRollback = await CollectAsync(
            store.ListUnlinkedAsync("orders", null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        afterRollback.Should().BeEmpty();

        using (var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false))
        {
            provider.Activate(connection, transaction);
            await store.StageAsync("orders", CreateDocument("committed"), CancellationToken.None).ConfigureAwait(false);
            provider.Deactivate();
            await transaction.CommitAsync().ConfigureAwait(false);
        }

        var afterCommit = await CollectAsync(
            store.ListUnlinkedAsync("orders", null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        afterCommit.Should().ContainSingle();
        afterCommit[0].Action.Should().Be("committed");
    }

    [SkippableFact]
    public async Task StageOutOfBandAsync_WithActiveAmbientTransaction_ShouldSurviveItsRollback()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var provider = new TestTransactionProvider();

        var store = new PostgreSqlAuditChainStore<PostgreSqlAuditRecord>(
            new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options),
            provider);

        using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        provider.Activate(connection, transaction);
        await store.StageOutOfBandAsync("orders", CreateDocument("refusal"), CancellationToken.None).ConfigureAwait(false);
        provider.Deactivate();
        await transaction.RollbackAsync().ConfigureAwait(false);

        var unlinked = await CollectAsync(
            store.ListUnlinkedAsync("orders", null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        unlinked.Should().ContainSingle();
        unlinked[0].Action.Should().Be("refusal");
    }

    [SkippableFact]
    public async Task UseExistingConnection_ShouldWriteInsideTheSuppliedTransaction()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlAuditChainStore<PostgreSqlAuditRecord>(
            new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options));

        using var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);

        var bound = store.UseExistingConnection(connection, transaction);
        await bound.StageAsync("orders", CreateDocument("bound"), CancellationToken.None).ConfigureAwait(false);
        await transaction.RollbackAsync().ConfigureAwait(false);

        var afterRollback = await CollectAsync(
            store.ListUnlinkedAsync("orders", null, CancellationToken.None),
            CancellationToken.None).ConfigureAwait(false);
        afterRollback.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task UseExistingConnection_WithMismatchedConnectionAndTransaction_ShouldThrow()
    {
        var options = CreateOptions();
        await PostgreSqlAuditSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var store = new PostgreSqlAuditChainStore<PostgreSqlAuditRecord>(
            new PostgreSqlAuditStoreRegistration(_fixture.DataSource, options));

        using var first = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var second = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        using var transaction = await first.BeginTransactionAsync().ConfigureAwait(false);

        var bind = () => store.UseExistingConnection(second, transaction);

        bind.Should().Throw<ArgumentException>();
    }

    [SkippableFact]
    public async Task GetLatestAsync_ShouldReturnTheHighestSequenceCheckpoint()
    {
        var store = (PostgreSqlAuditChainStore<PostgreSqlAuditRecord>) CreateStore();

        var missing = await store.GetLatestAsync("orders", CancellationToken.None).ConfigureAwait(false);
        missing.Should().BeNull();

        var first = new AuditCheckpoint(
            Guid.NewGuid(), "orders", 1, 1, "hash-1", "sha256-v2", AuditCheckpointKind.Regular, BaseTime);
        var second = new AuditCheckpoint(
            Guid.NewGuid(), "orders", 2, 2, "hash-2", "sha256-v2", AuditCheckpointKind.Regular, BaseTime.AddHours(1));
        var other = new AuditCheckpoint(
            Guid.NewGuid(), "payments", 5, 5, "hash-5", "sha256-v2", AuditCheckpointKind.Regular, BaseTime);

        await store.StoreCheckpointAsync(second, CancellationToken.None).ConfigureAwait(false);
        await store.StoreCheckpointAsync(first, CancellationToken.None).ConfigureAwait(false);
        await store.StoreCheckpointAsync(other, CancellationToken.None).ConfigureAwait(false);

        var latest = await store.GetLatestAsync("orders", CancellationToken.None).ConfigureAwait(false);
        latest.Should().NotBeNull();
        latest!.Id.Should().Be(second.Id);
        latest.Sequence.Should().Be(2);
    }

    [SkippableFact]
    public async Task AppendAsync_ToExpiredSegment_ShouldThrowAndKeepTheHead()
    {
        var store = (PostgreSqlAuditChainStore<PostgreSqlAuditRecord>) CreateStore();
        const string segment = "orders";

        var info = new AuditSegmentInfo(
            segment, null, new Dictionary<string, string>(StringComparer.Ordinal), BaseTime);
        var empty = new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
        var expired = new AuditSegmentHead(info, 1, "hash-1", 1, null, null, true, 1);
        var first = CreateDocument("record-1");
        first.Segment = segment;
        first.Sequence = 1;
        first.Hash = "hash-1";
        first.HashScheme = "sha256-v2";

        await store.AppendAsync(segment, empty, [first], expired, CancellationToken.None).ConfigureAwait(false);

        var second = CreateDocument("record-2");
        second.Segment = segment;
        second.Sequence = 2;
        second.PreviousHash = "hash-1";
        second.Hash = "hash-2";
        second.HashScheme = "sha256-v2";
        var moved = expired with { LastSequence = 2, LastHash = "hash-2", Count = 2, Version = 2 };

        var append = async () =>
        {
            await store.AppendAsync(segment, expired, [second], moved, CancellationToken.None).ConfigureAwait(false);
        };

        await append.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);

        var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        head.Should().NotBeNull();
        head!.Version.Should().Be(1);
        head.LastSequence.Should().Be(1);
    }

    [SkippableFact]
    public async Task AppendAsync_WhenDocumentIsAlreadyLinked_ShouldThrow()
    {
        var store = (PostgreSqlAuditChainStore<PostgreSqlAuditRecord>) CreateStore();
        const string segment = "orders";

        var info = new AuditSegmentInfo(
            segment, null, new Dictionary<string, string>(StringComparer.Ordinal), BaseTime);
        var empty = new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
        var document = CreateDocument("record-1");
        document.Segment = segment;
        document.Sequence = 1;
        document.Hash = "hash-1";
        document.HashScheme = "sha256-v2";
        var head = new AuditSegmentHead(info, 1, "hash-1", 1, null, null, false, 1);

        await store.AppendAsync(segment, empty, [document], head, CancellationToken.None).ConfigureAwait(false);

        var moved = head with { Version = 2 };

        var append = async () =>
        {
            await store.AppendAsync(segment, head, [document], moved, CancellationToken.None).ConfigureAwait(false);
        };

        await append.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task StageAsync_WhenDocumentCarriesAPosition_ShouldThrow()
    {
        var store = (PostgreSqlAuditChainStore<PostgreSqlAuditRecord>) CreateStore();
        var document = CreateDocument("record-1");
        document.Sequence = 1;

        var stage = async () =>
        {
            await store.StageAsync("orders", document, CancellationToken.None).ConfigureAwait(false);
        };

        await stage.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(false);
    }

    [SkippableFact]
    public async Task AppendAsync_ConcurrentFirstAppends_ShouldLeaveOneWinner()
    {
        var store = (PostgreSqlAuditChainStore<PostgreSqlAuditRecord>) CreateStore();
        const string segment = "orders";

        var info = new AuditSegmentInfo(
            segment, null, new Dictionary<string, string>(StringComparer.Ordinal), BaseTime);
        var expected = new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);

        var attempts = new Task<bool>[2];

        for (var index = 0; index < attempts.Length; index++)
        {
            var marker = "racer-" + index;
            var document = CreateDocument(marker);
            document.Segment = segment;
            document.Sequence = 1;
            document.Hash = "hash-" + marker;
            document.HashScheme = "sha256-v2";
            var updated = new AuditSegmentHead(info, 1, document.Hash, 1, null, null, false, 1);

            attempts[index] = TryAppendAsync(store, segment, expected, document, updated);
        }

        await Task.WhenAll(attempts).ConfigureAwait(false);

        var winners = 0;

        foreach (var attempt in attempts)
        {
            if (await attempt.ConfigureAwait(false))
            {
                winners++;
            }
        }

        winners.Should().Be(1);

        var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
        head.Should().NotBeNull();
        head!.Version.Should().Be(1);
        head.LastSequence.Should().Be(1);
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

    private static async Task<bool> TryAppendAsync(
        IAuditChainStore<PostgreSqlAuditRecord> store,
        string segment,
        AuditSegmentHead expected,
        PostgreSqlAuditRecord document,
        AuditSegmentHead updated)
    {
        try
        {
            await store.AppendAsync(segment, expected, [document], updated, CancellationToken.None).ConfigureAwait(false);

            return true;
        }
        catch (AuditHeadConflictException)
        {
            return false;
        }
    }

    private static async Task<IReadOnlyList<T>> CollectAsync<T>(IAsyncEnumerable<T> source, CancellationToken cancellationToken)
    {
        List<T> items = [];
        var enumerator = source.GetAsyncEnumerator(cancellationToken);

        try
        {
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                items.Add(enumerator.Current);
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }

        return items;
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
}
