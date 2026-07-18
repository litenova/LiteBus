using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

public sealed class PostgreSqlInboxSchemaTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlInboxSchemaTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnsureAsync_ShouldCreateSchemaAndBeIdempotent()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();

        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }

    [Fact]
    public async Task EnsureAsync_ShouldHandleConcurrentBootstrap()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options))
            .ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrowWhenTableIsMissing()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlSchemaComponents.Inbox).ConfigureAwait(false);
    }

}
