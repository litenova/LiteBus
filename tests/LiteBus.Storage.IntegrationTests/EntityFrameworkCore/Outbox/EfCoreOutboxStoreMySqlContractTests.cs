using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Runs shared outbox store contracts against Entity Framework Core with MySQL.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class EfCoreOutboxStoreMySqlContractTests : OutboxStoreContractTests
{
    /// <summary>
    ///     The MySQL fixture that owns the shared server.
    /// </summary>
    private readonly MySqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxStoreMySqlContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The MySQL fixture.</param>
    public EfCoreOutboxStoreMySqlContractTests(MySqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that MySQL leasing uses the database clock.
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
        EfCoreMySqlTestInfrastructure.ResetAsync(_fixture.ConnectionString).GetAwaiter().GetResult();
        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCoreMySqlTestInfrastructure.CreateContext(_fixture.ConnectionString)),
            EfCoreMySqlTestInfrastructure.StoreOptions);

        return new OutboxStoreContracts(store, store, store, store, store, store, store, store);
    }
}
