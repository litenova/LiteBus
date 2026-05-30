using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

/// <summary>
///     Script rendering tests that do not require Docker.
/// </summary>
public sealed class PostgreSqlSchemaScriptTests
{
    [Fact]
    public void InboxGetCreateScript_ShouldIncludeCurrentVersionObjects()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions("inbox_script_check");
        var script = PostgreSqlInboxSchema.GetCreateScript(options);

        script.Should().Contain(options.TableName);
        script.Should().Contain("trace_context");
        script.Should().Contain(options.MetadataTableName);
    }

    [Fact]
    public void InboxGetUpgradeScript_ShouldReturnVersion2Changes()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxOptions("inbox_upgrade_script");
        var script = PostgreSqlInboxSchema.GetUpgradeScript(1, 2, options);

        script.Should().Contain("trace_context");
    }

    [Fact]
    public void OutboxGetCreateScript_ShouldIncludeCurrentVersionObjects()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions("outbox_script_check");
        var script = PostgreSqlOutboxSchema.GetCreateScript(options);

        script.Should().Contain(options.TableName);
        script.Should().Contain("trace_context");
        script.Should().Contain(options.MetadataTableName);
    }

    [Fact]
    public void OutboxGetUpgradeScript_ShouldReturnVersion2Changes()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxOptions("outbox_upgrade_script");
        var script = PostgreSqlOutboxSchema.GetUpgradeScript(1, 2, options);

        script.Should().Contain("trace_context");
    }
}
