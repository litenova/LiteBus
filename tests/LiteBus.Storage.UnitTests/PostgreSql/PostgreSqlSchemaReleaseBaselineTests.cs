using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Saga.Storage.PostgreSql;

namespace LiteBus.Storage.UnitTests.PostgreSql;

/// <summary>
///     Verifies the PostgreSQL schema catalog published by the first LiteBus v6 release.
/// </summary>
public sealed class PostgreSqlSchemaReleaseBaselineTests
{
    /// <summary>
    ///     Verifies every durable component starts at version 1 and exposes no preview migration chain.
    /// </summary>
    [Fact]
    public void ComponentSchemas_ShouldPublishOnlyVersionOneFiles()
    {
        PostgreSqlInboxSchema.CurrentSchemaVersion.Should().Be(1);
        PostgreSqlInboxSchema.SqlFiles.Select(file => file.RelativePath).Should().Equal(
            PostgreSqlInboxSchemaSqlPaths.V1Create,
            PostgreSqlInboxSchemaSqlPaths.V1EnsureIndexes);

        PostgreSqlOutboxSchema.CurrentSchemaVersion.Should().Be(1);
        PostgreSqlOutboxSchema.SqlFiles.Select(file => file.RelativePath).Should().Equal(
            PostgreSqlOutboxSchemaSqlPaths.V1Create,
            PostgreSqlOutboxSchemaSqlPaths.V1EnsureIndexes);

        PostgreSqlSagaSchema.CurrentSchemaVersion.Should().Be(1);
        PostgreSqlSagaSchema.SqlFiles.Select(file => file.RelativePath).Should().Equal(
            PostgreSqlSagaSchemaSqlPaths.V1Create,
            PostgreSqlSagaSchemaSqlPaths.V1EnsureIndexes);
    }
}
