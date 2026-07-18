namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore;

/// <summary>
///     Shares one MySQL container between inbox and outbox provider contract tests.
/// </summary>
[CollectionDefinition(Name)]
public sealed class MySqlCollection : ICollectionFixture<MySqlFixture>
{
    /// <summary>
    ///     The xUnit collection name.
    /// </summary>
    public const string Name = "Entity Framework Core MySQL";
}
