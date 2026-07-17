using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Runs shared inbox store contracts against Entity Framework Core with file-backed SQLite.
/// </summary>
public sealed class EfCoreInboxStoreSqliteContractTests : InboxStoreContractTests, IClassFixture<SqliteDatabaseFixture>
{
    /// <summary>
    ///     The fixture that owns the isolated database file.
    /// </summary>
    private readonly SqliteDatabaseFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxStoreSqliteContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQLite fixture.</param>
    public EfCoreInboxStoreSqliteContractTests(SqliteDatabaseFixture fixture)
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
    protected override InboxStoreRoles CreateStore()
    {
        EfCoreSqliteTestInfrastructure.ResetAsync(_fixture.ConnectionString).GetAwaiter().GetResult();
        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(
                EfCoreSqliteTestInfrastructure.CreateContext(_fixture.ConnectionString)),
            EfCoreSqliteTestInfrastructure.StoreOptions);

        return new InboxStoreRoles(store, store, store, store, store, store, store, store);
    }
}
