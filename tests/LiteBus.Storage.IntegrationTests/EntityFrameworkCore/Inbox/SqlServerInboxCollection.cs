namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Serializes SQL Server-backed Entity Framework Core inbox tests that share one container fixture.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerInboxCollection : ICollectionFixture<SqlServerFixture>
{
    /// <summary>
    ///     The shared xUnit collection name.
    /// </summary>
    public const string Name = "EF Inbox SQL Server";
}
