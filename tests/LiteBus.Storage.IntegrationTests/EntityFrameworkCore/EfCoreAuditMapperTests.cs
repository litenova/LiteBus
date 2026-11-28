using LiteBus.Auditing.Abstractions;
using LiteBus.Auditing.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Verifies that the Entity Framework Core audit trail awaits the record mapper's asynchronous form with the context
///     the record is written on: the business context on the success path, and the isolated context that saves the
///     record on the out-of-band path.
/// </summary>
public sealed class EfCoreAuditMapperTests : IClassFixture<SqliteDatabaseFixture>
{
    /// <summary>
    ///     When the test records occurred.
    /// </summary>
    private static readonly DateTimeOffset OccurredAt = new(2026, 5, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    ///     The SQLite fixture.
    /// </summary>
    private readonly SqliteDatabaseFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreAuditMapperTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreAuditMapperTests(SqliteDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Success_record_is_mapped_with_the_business_context()
    {
        var mapper = new RecordingMapper();
        var action = $"orders.place-order-{Guid.NewGuid():N}";

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var trail = CreateTrail(business, mapper);

            await trail.WriteAsync(Record(action, AuditOutcome.Succeeded)).ConfigureAwait(false);
            await business.SaveChangesAsync().ConfigureAwait(false);

            mapper.OfferedContexts.Should().ContainSingle().Which.Should().BeSameAs(business);
        }

        (await CountAsync(action).ConfigureAwait(false)).Should().Be(1);
    }

    [Fact]
    public async Task Denied_record_is_mapped_with_the_isolated_context_that_saves_it()
    {
        var mapper = new RecordingMapper();
        var action = $"orders.refund-order-{Guid.NewGuid():N}";

        var business = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (business.ConfigureAwait(false))
        {
            var trail = CreateTrail(business, mapper);

            await trail.WriteAsync(Record(action, AuditOutcome.Denied)).ConfigureAwait(false);

            var offered = mapper.OfferedContexts.Should().ContainSingle().Subject;
            offered.Should().NotBeSameAs(business);
        }

        (await CountAsync(action).ConfigureAwait(false)).Should().Be(1);
    }

    /// <summary>
    ///     Builds the trail with the recording mapper.
    /// </summary>
    /// <param name="business">The business context.</param>
    /// <param name="mapper">The mapper.</param>
    /// <returns>The trail.</returns>
    private EfCoreAuditTrail<EfCoreAuditRecord> CreateTrail(EfCoreAuditTestDbContext business, RecordingMapper mapper)
    {
        return new EfCoreAuditTrail<EfCoreAuditRecord>(
            new EfCoreAuditChainStore<EfCoreAuditRecord>(new TestAuditDbContextFactory(_fixture.ConnectionString)),
            business,
            mapper,
            new EfCoreAuditRecordSegmenter());
    }

    /// <summary>
    ///     Creates a context over the fixture's database with the audit tables created.
    /// </summary>
    /// <returns>The context.</returns>
    private async Task<EfCoreAuditTestDbContext> CreatePreparedContextAsync()
    {
        var context = CreateContext(_fixture.ConnectionString);
        await context.Database.EnsureCreatedAsync().ConfigureAwait(false);

        return context;
    }

    /// <summary>
    ///     Counts the stored records of an action.
    /// </summary>
    /// <param name="action">The audit action.</param>
    /// <returns>The record count.</returns>
    private async Task<int> CountAsync(string action)
    {
        var context = await CreatePreparedContextAsync().ConfigureAwait(false);
        await using (context.ConfigureAwait(false))
        {
            return await context.Set<EfCoreAuditRecord>()
                .CountAsync(record => record.Action == action)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Creates an audit test context over a SQLite database.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <returns>The context.</returns>
    private static EfCoreAuditTestDbContext CreateContext(string connectionString)
    {
        return new EfCoreAuditTestDbContext(
            new DbContextOptionsBuilder<EfCoreAuditTestDbContext>()
                .UseSqlite(connectionString)
                .ReplaceService<IModelCacheKeyFactory, EfCoreAuditTestDbContext.ModelCacheKeyFactory>()
                .Options,
            new EntityFrameworkCoreAuditStoreOptions(),
            EfCoreStorageProvider.Sqlite);
    }

    /// <summary>
    ///     Builds an audit record with an outcome.
    /// </summary>
    /// <param name="action">The audit action.</param>
    /// <param name="outcome">The outcome.</param>
    /// <returns>The record.</returns>
    private static AuditRecord Record(string action, AuditOutcome outcome)
    {
        return new AuditRecord
        {
            Id = Guid.NewGuid(),
            Action = action,
            Outcome = outcome,
            OccurredAt = OccurredAt,
            Category = $"mapper-{Guid.NewGuid():N}"
        };
    }

    /// <summary>
    ///     A mapper that records the context the store offers it.
    /// </summary>
    private sealed class RecordingMapper : IAuditRecordMapper<EfCoreAuditRecord>
    {
        /// <summary>
        ///     The store's default mapper.
        /// </summary>
        private readonly EfCoreAuditRecordMapper _inner = new();

        /// <summary>
        ///     Gets the contexts the store offered, in order.
        /// </summary>
        public List<DbContext> OfferedContexts { get; } = [];

        /// <inheritdoc />
        public ValueTask<EfCoreAuditRecord> MapAsync(
            AuditRecord record,
            AuditMappingContext context,
            CancellationToken cancellationToken)
        {
            OfferedContexts.Add(context.Features.Get<DbContext>());

            return ValueTask.FromResult(_inner.Map(record));
        }
    }

    /// <summary>
    ///     Opens isolated audit contexts over the fixture's database.
    /// </summary>
    private sealed class TestAuditDbContextFactory : IAuditDbContextFactory
    {
        /// <summary>
        ///     The connection string.
        /// </summary>
        private readonly string _connectionString;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TestAuditDbContextFactory" /> class.
        /// </summary>
        /// <param name="connectionString">The connection string.</param>
        public TestAuditDbContextFactory(string connectionString)
        {
            _connectionString = connectionString;
        }

        /// <inheritdoc />
        public IAuditDbContext CreateDbContext()
        {
            var context = CreateContext(_connectionString);
            context.Database.EnsureCreated();

            return context;
        }
    }
}
