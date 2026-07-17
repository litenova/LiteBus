using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Verifies that PostgreSQL schema validation rejects incompatible column types.
/// </summary>
public sealed class PostgreSqlSchemaColumnTypeTests : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    ///     The PostgreSQL fixture used to create isolated schemas.
    /// </summary>
    private readonly PostgreSqlFixture _fixture;

    /// <summary>
    ///     Initializes a new instance of the <see cref="PostgreSqlSchemaColumnTypeTests" /> class.
    /// </summary>
    /// <param name="fixture">The PostgreSQL fixture.</param>
    public PostgreSqlSchemaColumnTypeTests(PostgreSqlFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>
    ///     Verifies that an inbox payload column reverted to jsonb fails current-schema validation.
    /// </summary>
    /// <returns>A task that represents the asynchronous test.</returns>
    [Fact]
    public async Task InboxValidateAsync_WhenPayloadTypeIsJsonb_ShouldReportTypeDrift()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions();
        await PostgreSqlInboxSchema.EnsureAsync(_fixture.DataSource, options).ConfigureAwait(false);

        var connection = await _fixture.DataSource.OpenConnectionAsync().ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            var command = connection.CreateCommand();
            await using (command.ConfigureAwait(false))
            {
                command.CommandText = $"""
                                      ALTER TABLE "{options.SchemaName}"."{options.TableName}"
                                          ALTER COLUMN payload TYPE jsonb USING payload::jsonb;
                                      """;
                await command.ExecuteNonQueryAsync().ConfigureAwait(false);
            }
        }

        var action = async () => await PostgreSqlInboxSchema.ValidateAsync(_fixture.DataSource, options).ConfigureAwait(false);

        await action.Should().ThrowAsync<PostgreSqlSchemaDriftException>()
            .Where(exception =>
                exception.Component == PostgreSqlSchemaComponents.Inbox &&
                exception.Details.Contains("payload expected text but found jsonb", StringComparison.Ordinal))
            .ConfigureAwait(false);
    }
}
