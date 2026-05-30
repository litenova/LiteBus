using LiteBus.Storage.PostgreSql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

public sealed class PostgreSqlIdentifierTests
{
    [Fact]
    public void Quote_ShouldEscapeEmbeddedQuotes()
    {
        var quoted = PostgreSqlIdentifier.Quote("app\"name");

        quoted.Should().Be("\"app\"\"name\"");
    }

    [Fact]
    public void Qualify_ShouldReturnQuotedSchemaAndTable()
    {
        var qualified = PostgreSqlIdentifier.Qualify("litebus_tests", "inbox_commands");

        qualified.Should().Be("\"litebus_tests\".\"inbox_commands\"");
    }

    [Fact]
    public void StableHash_ShouldBeDeterministicForSameInput()
    {
        var first = PostgreSqlIdentifier.StableHash("litebus:inbox:public:table");
        var second = PostgreSqlIdentifier.StableHash("litebus:inbox:public:table");

        first.Should().Be(second);
    }

    [Fact]
    public void IndexName_ShouldTrimOverlongIdentifiers()
    {
        var tableName = new string('a', 80);
        var indexName = PostgreSqlIdentifier.IndexName(tableName, "pending_lease_idx");

        indexName.Length.Should().BeLessThanOrEqualTo(62);
        indexName.Should().StartWith("\"");
        indexName.Should().EndWith("\"");
    }
}
