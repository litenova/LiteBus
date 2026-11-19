using LiteBus.Auditing.Storage.EntityFrameworkCore;
using LiteBus.Auditing.Abstractions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Storage.EntityFrameworkCore;
using LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     PostgreSQL integration tests for the Entity Framework Core audit chain store: concurrent appends
///     assign unique sequences, and the schema-qualified mapping round-trips heads, documents, and
///     checkpoints.
/// </summary>
public sealed class EfCoreAuditChainStorePostgreSqlTests : IClassFixture<PostgreSqlFixture>
{
    private static readonly DateTimeOffset OpenedAt = new(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreAuditChainStorePostgreSqlTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public EfCoreAuditChainStorePostgreSqlTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    [SkippableFact]
    public async Task Concurrent_appends_assign_unique_sequences_without_gaps()
    {
        var segment = UniqueSegment("concurrent");
        var info = SegmentInfo(segment, "tenant-a");

        var appends = Enumerable.Range(0, 8).Select(index => Task.Run(async () =>
        {
            var context = await CreatePreparedContextAsync().ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                var store = new EfCoreAuditChainStore<EfCoreAuditRecord>(context);
                var document = LinkedDocument(segment, 0, null, $"hash-{index}");

                for (var attempt = 0; attempt < 50; attempt++)
                {
                    var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
                    var expected = head ?? new AuditSegmentHead(info, 0, null, 0, null, null, false, 0);
                    var sequence = expected.LastSequence + 1;
                    document.Sequence = sequence;
                    document.PreviousHash = expected.LastHash;
                    var updated = expected with
                    {
                        LastSequence = sequence,
                        LastHash = document.Hash,
                        Count = expected.Count + 1,
                        Version = expected.Version + 1
                    };

                    try
                    {
                        await store
                            .AppendAsync(segment, expected, [document], updated, CancellationToken.None)
                            .ConfigureAwait(false);

                        return;
                    }
                    catch (AuditHeadConflictException)
                    {
                        // Another writer committed first; the next attempt reads the winner.
                    }
                }

                throw new InvalidOperationException($"The append for document {index} never committed.");
            }
        })).ToList();

        await Task.WhenAll(appends).ConfigureAwait(false);

        var verify = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (verify.ConfigureAwait(false))
        {
            var store = new EfCoreAuditChainStore<EfCoreAuditRecord>(verify);
            var stored = await CollectAsync(
                    store.ReadSegmentAsync(segment, 1, null, CancellationToken.None))
                .ConfigureAwait(false);

            stored.Select(document => document.Sequence).Should().Equal(1L, 2L, 3L, 4L, 5L, 6L, 7L, 8L);

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head!.LastSequence.Should().Be(8);
            head.Count.Should().Be(8);
            head.Version.Should().Be(8);
            head.LastHash.Should().Be(stored[7].Hash);
        }
    }

    [SkippableFact]
    public async Task Append_checkpoint_and_erase_round_trip_on_postgresql()
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            var store = new EfCoreAuditChainStore<EfCoreAuditRecord>(context);
            var tenant = $"tenant-{Guid.NewGuid():N}";
            var segment = UniqueSegment("postgres");
            var expected = InitialHead(segment, tenant);
            var updated = expected with { LastSequence = 1, LastHash = "hash-1", Count = 1, Version = 1 };

            await store
                .AppendAsync(segment, expected, [LinkedDocument(segment, 1, null, "hash-1")], updated, CancellationToken.None)
                .ConfigureAwait(false);

            var checkpointId = Guid.NewGuid();

            await store
                .StoreCheckpointAsync(
                    new AuditCheckpoint(checkpointId, segment, 1, 1, "hash-1", "sha256-v2", AuditCheckpointKind.Regular, OpenedAt),
                    CancellationToken.None)
                .ConfigureAwait(false);

            var checkpoint = await store.ReadCheckpointAsync(checkpointId, CancellationToken.None).ConfigureAwait(false);
            checkpoint.Should().NotBeNull();
            checkpoint!.CreatedAt.Should().Be(OpenedAt);

            await store.MarkPublishedAsync(checkpointId, null, CancellationToken.None).ConfigureAwait(false);

            var published = await store.ReadCheckpointAsync(checkpointId, CancellationToken.None).ConfigureAwait(false);
            published!.PublishState.Should().Be(CheckpointPublishState.Published);

            await store
                .EraseAsync(new AuditSegmentFilter(TenantId: tenant), CancellationToken.None)
                .ConfigureAwait(false);

            var head = await store.ReadHeadAsync(segment, CancellationToken.None).ConfigureAwait(false);
            head.Should().BeNull();

            var erased = await store.ReadCheckpointAsync(checkpointId, CancellationToken.None).ConfigureAwait(false);
            erased.Should().BeNull();
        }
    }

    private async Task<EfCoreAuditTestDbContext> CreatePreparedContextAsync()
    {
        var storeOptions = new EntityFrameworkCoreAuditStoreOptions { SchemaName = "ef_audit_tests" };
        var context = new EfCoreAuditTestDbContext(
            new DbContextOptionsBuilder<EfCoreAuditTestDbContext>()
                .UseNpgsql(_fixture.ConnectionString)
                .ReplaceService<IModelCacheKeyFactory, EfCoreAuditTestDbContext.ModelCacheKeyFactory>()
                .Options,
            storeOptions,
            EfCoreStorageProvider.PostgreSql);

        await context.Database.ExecuteSqlRawAsync("""CREATE SCHEMA IF NOT EXISTS "ef_audit_tests";""").ConfigureAwait(false);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        return context;
    }

    private static string UniqueSegment(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static AuditSegmentInfo SegmentInfo(string segment, string tenant)
    {
        return new AuditSegmentInfo(
            segment,
            tenant,
            new Dictionary<string, string>
            {
                ["category"] = "orders",
                ["year"] = "2026"
            },
            OpenedAt);
    }

    private static AuditSegmentHead InitialHead(string segment, string tenant)
    {
        return new AuditSegmentHead(SegmentInfo(segment, tenant), 0, null, 0, null, null, false, 0);
    }

    private static EfCoreAuditRecord LinkedDocument(string segment, long sequence, string? previousHash, string hash)
    {
        return new EfCoreAuditRecord
        {
            Id = Guid.NewGuid(),
            Action = "orders.place-order",
            Outcome = AuditOutcome.Succeeded,
            OccurredAt = OpenedAt,
            Category = "orders",
            TenantId = "tenant-a",
            Segment = segment,
            Sequence = sequence,
            PreviousHash = previousHash,
            Hash = hash,
            HashScheme = "sha256-v2"
        };
    }

    private static async Task<List<T>> CollectAsync<T>(IAsyncEnumerable<T> source)
    {
        List<T> items = [];

        await foreach (var item in source.ConfigureAwait(false))
        {
            items.Add(item);
        }

        return items;
    }
}
