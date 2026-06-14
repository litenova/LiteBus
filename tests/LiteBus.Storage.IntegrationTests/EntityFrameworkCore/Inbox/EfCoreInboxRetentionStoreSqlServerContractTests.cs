using LiteBus.Storage.Testing;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Runs inbox retention contract tests against Entity Framework Core with SQL Server.
/// </summary>
public sealed class EfCoreInboxRetentionStoreSqlServerContractTests : InboxRetentionStoreContractTests, IClassFixture<SqlServerFixture>
{
    /// <summary>
    ///     The SQL Server fixture that supplies the connection string.
    /// </summary>
    private readonly SqlServerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxRetentionStoreSqlServerContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQL Server fixture.</param>
    public EfCoreInboxRetentionStoreSqlServerContractTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override InboxStoreRoles CreateStore()
    {
        EfCoreSqlServerTestInfrastructure.ResetInboxTableAsync(_fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCoreSqlServerTestInfrastructure.InboxStoreOptions;

        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(
                EfCoreSqlServerTestInfrastructure.CreateInboxContext(_fixture.ConnectionString)),
            options);

        return new InboxStoreRoles(store, store, store, store, store);
    }
}
