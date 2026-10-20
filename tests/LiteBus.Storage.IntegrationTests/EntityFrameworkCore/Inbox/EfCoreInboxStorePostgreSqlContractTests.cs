using LiteBus.Storage.Testing;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;

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
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies that Entity Framework PostgreSQL leasing ignores a skewed caller clock.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [SkippableFact]
    public Task LeasePendingAsync_WhenCallerClockIsSkewed_ShouldUseDatabaseClock()
    {
        return AssertDatabaseClockIgnoresCallerSkewAsync();
    }

    /// <inheritdoc />
    protected override InboxStoreContractRoles CreateStore()
    {
        EfCoreProviderTestInfrastructure.ResetInboxTableAsync(EfCoreStorageProvider.PostgreSql, _fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCoreProviderTestInfrastructure.InboxStoreOptions(EfCoreStorageProvider.PostgreSql);

        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(
                EfCoreProviderTestInfrastructure.CreateInboxContext(EfCoreStorageProvider.PostgreSql, _fixture.ConnectionString)),
            options);

        return new InboxStoreContractRoles(store, store, store, store, store, store, store, store);
    }
}
