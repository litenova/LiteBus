using LiteBus.Inbox.Storage.PostgreSql;
using LiteBus.Outbox.Storage.PostgreSql;
using LiteBus.Storage.PostgreSql;
using LiteBus.Inbox;
using LiteBus.Outbox;
using LiteBus.Messaging;

namespace LiteBus.Storage.IntegrationTests.PostgreSql;

/// <summary>
///     Script rendering tests that do not require Docker.
/// </summary>
public sealed class PostgreSqlSchemaScriptTests
{
    [Fact]
    public void InboxGetCreateScript_ShouldIncludeCurrentVersionObjects()
    {
        var options = PostgreSqlTestInfrastructure.CreateInboxStoreOptions("inbox_script_check");
        var script = PostgreSqlInboxSchema.GetCreateScript(options);

        script.Should().Contain(options.TableName);
        script.Should().Contain("trace_context");
        script.Should().Contain(options.MetadataTableName);
    }

    [Fact]
    public void OutboxGetCreateScript_ShouldIncludeCurrentVersionObjects()
    {
        var options = PostgreSqlTestInfrastructure.CreateOutboxStoreOptions("outbox_script_check");
        var script = PostgreSqlOutboxSchema.GetCreateScript(options);

        script.Should().Contain(options.TableName);
        script.Should().Contain("trace_context");
        script.Should().Contain(options.MetadataTableName);
    }
}