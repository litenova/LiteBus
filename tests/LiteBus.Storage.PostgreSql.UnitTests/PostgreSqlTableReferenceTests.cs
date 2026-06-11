using LiteBus.Inbox.Storage.PostgreSql;

namespace LiteBus.Storage.PostgreSql.UnitTests;

public sealed class PostgreSqlTableReferenceTests
{
    [Fact]
    public void Create_exposes_schema_and_table_names()
    {
        var table = PostgreSqlTableReference.Create("app", "inbox_messages");

        table.SchemaName.Should().Be("app");
        table.TableName.Should().Be("inbox_messages");
        table.QualifiedName.Should().Be("app.inbox_messages");
        table.QuotedQualifiedName.Should().Be("\"app\".\"inbox_messages\"");
    }

    [Fact]
    public void ForStore_reads_names_from_options()
    {
        var options = new PostgreSqlInboxStoreOptions
        {
            SchemaName = "orders",
            TableName = "inbox_orders"
        };

        var table = PostgreSqlTableReference.ForStore(options);

        table.QualifiedName.Should().Be("orders.inbox_orders");
    }

    [Fact]
    public void ForMetadata_reads_metadata_names_from_options()
    {
        var options = new PostgreSqlInboxStoreOptions
        {
            MetadataSchemaName = "litebus",
            MetadataTableName = "schema_versions"
        };

        var table = PostgreSqlTableReference.ForMetadata(options);

        table.QualifiedName.Should().Be("litebus.schema_versions");
    }

    [Theory]
    [InlineData(null, "table")]
    [InlineData("schema", null)]
    [InlineData("", "table")]
    [InlineData("schema", "   ")]
    public void Create_rejects_blank_identifiers(string? schemaName, string? tableName)
    {
        var action = () => PostgreSqlTableReference.Create(schemaName!, tableName!);

        action.Should().Throw<ArgumentException>();
    }
}