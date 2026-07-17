using LiteBus.Storage.Testing;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Runs shared inbox store contract tests against Entity Framework Core with PostgreSQL.
/// </summary>
public sealed class EfCoreInboxStorePostgreSqlContractTests : InboxStoreContractTests, IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture that supplies the connection string.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxStorePostgreSqlContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreInboxStorePostgreSqlContractTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that Entity Framework PostgreSQL leasing ignores a skewed caller clock.
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
        EfCorePostgreSqlTestInfrastructure.ResetInboxTableAsync(_fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCorePostgreSqlTestInfrastructure.InboxStoreOptions;

        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(
                EfCorePostgreSqlTestInfrastructure.CreateInboxContext(_fixture.ConnectionString)),
            options);

        return new InboxStoreRoles(store, store, store, store, store, store, store, store);
    }
}
