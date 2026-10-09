using LiteBus.Storage.Testing;
using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Runs outbox retention contract tests against Entity Framework Core with PostgreSQL.
/// </summary>
[Collection(PostgreSqlCollection.Name)]
public sealed class EfCoreOutboxRetentionStorePostgreSqlContractTests : OutboxRetentionStoreContractTests, IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture that supplies the connection string.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="EfCoreOutboxRetentionStorePostgreSqlContractTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public EfCoreOutboxRetentionStorePostgreSqlContractTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <inheritdoc />
    protected override OutboxStoreContracts CreateStore()
    {
        EfCoreProviderTestInfrastructure.ResetOutboxTableAsync(EfCoreStorageProvider.PostgreSql, _fixture.ConnectionString)
            .GetAwaiter()
            .GetResult();

        var options = EfCoreProviderTestInfrastructure.OutboxStoreOptions(EfCoreStorageProvider.PostgreSql);

        var store = new EfCoreOutboxStore(
            _ => Task.FromResult<IOutboxDbContext>(
                EfCoreProviderTestInfrastructure.CreateOutboxContext(EfCoreStorageProvider.PostgreSql, _fixture.ConnectionString)),
            options);

        return new OutboxStoreContracts(store, store, store, store, store);
    }
}
