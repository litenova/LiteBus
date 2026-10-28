using LiteBus.Outbox.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies a bound PostgreSQL outbox store reports the parent custom schema options.
/// </summary>
public sealed class PostgreSqlOutboxBoundStoreOptionsTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    public PostgreSqlOutboxBoundStoreOptionsTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
        Skip.IfNot(_fixture.IsAvailable, PostgreSqlFixture.DockerRequiredMessage);
    }

    /// <summary>
    ///     Verifies GetSchemaInfoAsync on a bound store returns the custom schema and table names.
    /// </summary>
    [SkippableFact]
    public async Task GetSchemaInfoAsync_on_bound_store_should_report_custom_schema()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions();
        await PostgreSqlTestInfrastructure.EnsureOutboxSchemaAsync(_fixture.DataSource, options).ConfigureAwait(false);
        var store = new PostgreSqlOutboxStore(_fixture.DataSource, options);

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using var connectionScope = connection.ConfigureAwait(false);
        var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await using var transactionScope = transaction.ConfigureAwait(false);
        var bound = (PostgreSqlOutboxStore) store.UseExistingConnection(connection, transaction);

        var info = await bound.GetSchemaInfoAsync().ConfigureAwait(false);

        info.SchemaName.Should().Be(options.SchemaName);
        info.TableName.Should().Be(options.TableName);
        await transaction.RollbackAsync().ConfigureAwait(false);
    }
}
