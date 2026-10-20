using LiteBus.Storage.Testing;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Runs inbox retention contract tests against Entity Framework Core with SQL Server.
/// </summary>
[Collection(SqlServerInboxCollection.Name)]
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
        Skip.IfNot(_fixture.IsAvailable, SqlServerFixture.DockerRequiredMessage);
    }

    /// <inheritdoc />
    protected override InboxRetentionStoreContractRoles CreateStore()
    {
        EfCoreProviderTestInfrastructure.ResetInboxTableAsync(EfCoreStorageProvider.SqlServer, _fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCoreProviderTestInfrastructure.InboxStoreOptions(EfCoreStorageProvider.SqlServer);

        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(
                EfCoreProviderTestInfrastructure.CreateInboxContext(EfCoreStorageProvider.SqlServer, _fixture.ConnectionString)),
            options);

        return new InboxRetentionStoreContractRoles(store, store, store, store, store);
    }
}
