using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Runs shared inbox store contracts against Entity Framework Core with MySQL.
/// </summary>
[Collection(MySqlCollection.Name)]
public sealed class EfCoreInboxStoreMySqlContractTests : InboxStoreContractTests
{
    /// <summary>
    ///     The MySQL fixture that owns the shared server.
    /// </summary>
    private readonly MySqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxStoreMySqlContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The MySQL fixture.</param>
    public EfCoreInboxStoreMySqlContractTests(MySqlFixture fixture)
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
    protected override InboxStoreRoles CreateStore()
    {
        EfCoreMySqlTestInfrastructure.ResetAsync(_fixture.ConnectionString).GetAwaiter().GetResult();
        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(
                EfCoreMySqlTestInfrastructure.CreateContext(_fixture.ConnectionString)),
            EfCoreMySqlTestInfrastructure.StoreOptions);

        return new InboxStoreRoles(store, store, store, store, store, store, store, store);
    }
}
