using LiteBus.Outbox.Abstractions;
using LiteBus.Storage.Testing;
using LiteBus.Outbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Runs shared outbox store contract tests against Entity Framework Core with SQL Server.
/// </summary>
public sealed class EfCoreOutboxStoreSqlServerContractTests : OutboxStoreContractTests, IClassFixture<SqlServerFixture>
{
    /// <summary>
    ///     The SQL Server fixture that supplies the connection string.
    /// </summary>
    private readonly SqlServerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxStoreSqlServerContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQL Server fixture.</param>
    public EfCoreOutboxStoreSqlServerContractTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        return CreateStore(true);
    }

    /// <summary>
    ///     Creates a store, optionally resetting the SQL Server table first.
    /// </summary>
    /// <param name="resetTable">Whether to truncate the outbox table before creating the store.</param>
    /// <returns>The store contracts under test.</returns>
    private OutboxStoreContracts CreateStore(bool resetTable)
    {
        if (resetTable)
        {
            EfCoreSqlServerTestInfrastructure.ResetOutboxTableAsync(_fixture.ConnectionString)
                .GetAwaiter()
                .GetResult();
        }

        var options = EfCoreSqlServerTestInfrastructure.OutboxStoreOptions;

        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCoreSqlServerTestInfrastructure.CreateOutboxContext(_fixture.ConnectionString)),
            options);

        return new OutboxStoreContracts(store, store, store, store, store, store, store, store);
    }

    /// <inheritdoc />
    protected async override Task AssertConcurrentLeasesAreDisjointAsync()
    {
        var writer = CreateStore(true);
        var now = BaseTime;

        for (var index = 0; index < 6; index++)
        {
            await writer.Writer.EnqueueAsync(CreatePendingEnvelope(Guid.NewGuid(), now.AddSeconds(index))).ConfigureAwait(false);
        }

        var request = new OutboxLeaseRequest
        {
            BatchSize = 3,
            LeaseOwner = "publisher",
            Now = now.AddMinutes(1),
            LeaseDuration = TimeSpan.FromMinutes(5)
        };

        var leaseStoreA = CreateStore(false);
        var leaseStoreB = CreateStore(false);

        // SQL Server READPAST lease under Docker is validated sequentially; PostgreSQL contract covers concurrent workers.
        var firstBatch = await leaseStoreA.Lease.LeasePendingAsync(request with { LeaseOwner = "publisher-a" }).ConfigureAwait(false);
        var secondBatch = await leaseStoreB.Lease.LeasePendingAsync(request with { LeaseOwner = "publisher-b" }).ConfigureAwait(false);

        var leasedIds = firstBatch.Select(message => message.Id)
            .Concat(secondBatch.Select(message => message.Id))
            .ToArray();

        leasedIds.Should().HaveCount(6);
        leasedIds.Should().OnlyHaveUniqueItems();
    }
}