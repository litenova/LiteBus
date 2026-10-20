namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Shared;

/// <summary>
///     Verifies relational table qualification escapes provider quote characters.
/// </summary>
public sealed class EfCoreRelationalTableQualifierTests
{
    /// <summary>
    ///     Confirms PostgreSQL identifiers double embedded quotes.
    /// </summary>
    [Fact]
    public void Qualify_WithPostgreSqlQuoteInName_ShouldEscapeQuotes()
    {
        var qualified = EfCoreRelationalTableQualifier.Qualify(EfCoreStorageProvider.PostgreSql, "my\"schema", "my\"table");

        qualified.Should().Be("\"my\"\"schema\".\"my\"\"table\"");
    }

    /// <summary>
    ///     Confirms SQL Server identifiers double embedded closing brackets.
    /// </summary>
    [Fact]
    public void Qualify_WithSqlServerBracketInName_ShouldEscapeBrackets()
    {
        var qualified = EfCoreRelationalTableQualifier.Qualify(EfCoreStorageProvider.SqlServer, "my]schema", "my]table");

        qualified.Should().Be("[my]]schema].[my]]table]");
    }

    /// <summary>
    ///     Confirms MySQL identifiers double embedded backticks.
    /// </summary>
    [Fact]
    public void Qualify_WithMySqlBacktickInName_ShouldEscapeBackticks()
    {
        var qualified = EfCoreRelationalTableQualifier.Qualify(EfCoreStorageProvider.MySql, "ignored", "my`table");

        qualified.Should().Be("`my``table`");
    }

    /// <summary>
    ///     Confirms SQLite identifiers double embedded quotes.
    /// </summary>
    [Fact]
    public void Qualify_WithSqliteQuoteInName_ShouldEscapeQuotes()
    {
        var qualified = EfCoreRelationalTableQualifier.Qualify(EfCoreStorageProvider.Sqlite, "ignored", "my\"table");

        qualified.Should().Be("\"my\"\"table\"");
    }
}
