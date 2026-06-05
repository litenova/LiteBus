using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.Testing;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.IntegrationTests;

/// <summary>
///     Runs shared inbox store contract tests against Entity Framework Core with SQL Server.
/// </summary>
public sealed class EfCoreInboxStoreSqlServerContractTests : InboxStoreContractTests, IClassFixture<SqlServerFixture>
{
    /// <summary>
    ///     The SQL Server fixture that supplies the connection string.
    /// </summary>
    private readonly SqlServerFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxStoreSqlServerContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The SQL Server fixture.</param>
    public EfCoreInboxStoreSqlServerContractTests(SqlServerFixture fixture)
    {
        _fixture = fixture;
    }

    /// <inheritdoc />
    protected override InboxStoreRoles CreateStore()
    {
        EfCoreSqlServerTestInfrastructure.ResetInboxTableAsync(_fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCoreSqlServerTestInfrastructure.InboxOptions;
        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(
                EfCoreSqlServerTestInfrastructure.CreateInboxContext(_fixture.ConnectionString)),
            options);
        return new InboxStoreRoles(store, store, store, store, store);
    }
}
