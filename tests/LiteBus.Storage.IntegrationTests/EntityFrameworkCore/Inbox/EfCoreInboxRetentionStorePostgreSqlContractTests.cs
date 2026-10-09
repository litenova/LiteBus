using LiteBus.Storage.Testing;
using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Runs inbox retention contract tests against Entity Framework Core with PostgreSQL.
/// </summary>
public sealed class EfCoreInboxRetentionStorePostgreSqlContractTests : InboxRetentionStoreContractTests, IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture that supplies the connection string.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreInboxRetentionStorePostgreSqlContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreInboxRetentionStorePostgreSqlContractTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <inheritdoc />
    protected override InboxRetentionStoreContractRoles CreateStore()
    {
        EfCoreProviderTestInfrastructure.ResetInboxTableAsync(EfCoreStorageProvider.PostgreSql, _fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCoreProviderTestInfrastructure.InboxStoreOptions(EfCoreStorageProvider.PostgreSql);

        var store = new EfCoreInboxStore(
            _ => Task.FromResult<IInboxDbContext>(
                EfCoreProviderTestInfrastructure.CreateInboxContext(EfCoreStorageProvider.PostgreSql, _fixture.ConnectionString)),
            options);

        return new InboxRetentionStoreContractRoles(store, store, store, store, store);
    }
}
