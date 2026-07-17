using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Runs shared outbox store contracts against Entity Framework Core with file-backed SQLite.
/// </summary>
public sealed class EfCoreOutboxStoreSqliteContractTests : OutboxStoreContractTests, IClassFixture<SqliteDatabaseFixture>
{
    /// <summary>
    ///     The fixture that owns the isolated database file.
    /// </summary>
    private readonly SqliteDatabaseFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxStoreSqliteContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreOutboxStoreSqliteContractTests(SqliteDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that SQLite leasing uses the database clock.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public Task LeasePendingAsync_WhenCallerClockIsSkewed_ShouldUseDatabaseClock()
    {
        return AssertDatabaseClockIgnoresCallerSkewAsync();
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        EfCoreSqliteTestInfrastructure.ResetAsync(_fixture.ConnectionString).GetAwaiter().GetResult();
        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCoreSqliteTestInfrastructure.CreateContext(_fixture.ConnectionString)),
            EfCoreSqliteTestInfrastructure.StoreOptions);

        return new OutboxStoreContracts(store, store, store, store, store, store, store, store);
    }
}
