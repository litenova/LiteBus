using LiteBus.Storage.PostgreSql.IntegrationTests;

namespace LiteBus.Saga.Storage.PostgreSql.UnitTests;

/// <summary>
///     Verifies PostgreSQL saga schema ensure and validate against a live container.
/// </summary>
public sealed class PostgreSqlSagaSchemaTests : IClassFixture<PostgreSqlFixture>
{
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSagaSchemaTests" /> class.
    /// </summary>
    /// <param name="fixture">The shared PostgreSQL container fixture.</param>
    public PostgreSqlSagaSchemaTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies saga schema ensure creates tables required by <see cref="PostgreSqlSagaStore" />.
    /// </summary>
    [Fact]
    public async Task EnsureAsync_ShouldCreateSagaSchema()
    {
        var options = new PostgreSqlSagaStoreOptions
        {
            TableName = $"saga_test_{Guid.NewGuid():N}"
        };

        await PostgreSqlSagaSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(true);

        var action = async () => await PostgreSqlSagaSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(true);
        await action.Should().NotThrowAsync();
    }
}
