using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Saga.Storage.PostgreSql;

namespace LiteBus.Storage.UnitTests.PostgreSql;

/// <summary>
///     Verifies the current PostgreSQL schema catalog.
/// </summary>
public sealed class PostgreSqlSchemaReleaseBaselineTests
{
    /// <summary>
    ///     Verifies every durable component publishes its current schema version and files.
    /// </summary>
    [Fact]
    public void ComponentSchemas_ShouldPublishCurrentVersionFiles()
    {
        PostgreSqlInboxSchema.CurrentSchemaVersion.Should().Be(1);
        PostgreSqlInboxSchema.SqlFiles.Select(file => file.RelativePath).Should().Equal(
            PostgreSqlInboxSchemaSqlPaths.V1Create,
            PostgreSqlInboxSchemaSqlPaths.V1EnsureIndexes);

        PostgreSqlOutboxSchema.CurrentSchemaVersion.Should().Be(2);
        PostgreSqlOutboxSchema.SqlFiles.Select(file => file.RelativePath).Should().Equal(
            PostgreSqlOutboxSchemaSqlPaths.V2Create,
            PostgreSqlOutboxSchemaSqlPaths.V1EnsureIndexes,
            PostgreSqlOutboxSchemaSqlPaths.V2Upgrade);

        PostgreSqlSagaSchema.CurrentSchemaVersion.Should().Be(1);
        PostgreSqlSagaSchema.SqlFiles.Select(file => file.RelativePath).Should().Equal(
            PostgreSqlSagaSchemaSqlPaths.V1Create,
            PostgreSqlSagaSchemaSqlPaths.V1EnsureIndexes);
    }
}
