using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

public sealed class PostgreSqlOutboxSchemaTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxSchemaTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task EnsureAsync_ShouldCreateSchemaAndBeIdempotent()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();

        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }

    [Fact]
    public async Task EnsureAsync_ShouldHandleConcurrentBootstrap()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();

        var tasks = Enumerable.Range(0, 5)
            .Select(_ => PostgreSqlOutboxSchema.EnsureAsync(_fixture.DataSource, options))
            .ToArray();

        await Task.WhenAll(tasks).ConfigureAwait(false);
        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }

    [Fact]
    public async Task ValidateAsync_ShouldThrowWhenTableIsMissing()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();

        var action = async () => await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception => exception.Component == PostgreSqlSchemaComponents.Outbox).ConfigureAwait(false);
    }

    [Fact]
    public async Task CreateIfNotExistsAsync_ShouldDelegateToEnsureAsync()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();

        await PostgreSqlOutboxSchema.CreateIfNotExistsAsync(_fixture.DataSource, options).ConfigureAwait(false);
        await PostgreSqlOutboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);
    }
}