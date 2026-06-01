using LiteBus.Storage.PostgreSql;

namespace LiteBus.Storage.PostgreSql.IntegrationTests;

public sealed class PostgreSqlStorageUtilityTests
{
    [Fact]
    public void CreateFromConnectionString_ShouldReturnDataSource()
    {
        using var dataSource = PostgreSqlDataSourceFactory.CreateFromConnectionString(
            "Host=localhost;Database=litebus;Username=app;Password=secret");

        dataSource.Should().NotBeNull();
    }

    [Fact]
    public void CreateFromConnectionString_WhenConnectionStringMissing_ShouldThrow()
    {
        var act = () => PostgreSqlDataSourceFactory.CreateFromConnectionString(" ");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SchemaSqlPaths_ShouldExposeCanonicalFiles()
    {
        PostgreSqlSchemaSqlPaths.Files.Should().NotBeEmpty();
        PostgreSqlSchemaSqlPaths.Files.Should().Contain(file =>
            file.RelativePath == PostgreSqlSchemaSqlPaths.MetadataCreate);
    }
}
