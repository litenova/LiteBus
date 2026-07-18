namespace LiteBus.Storage.IntegrationTests.EntityFrameworkCore.Outbox;

/// <summary>
///     Serializes SQL Server-backed Entity Framework Core outbox tests that share one container fixture.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SqlServerOutboxCollection : ICollectionFixture<SqlServerFixture>
{
    /// <summary>
    ///     The shared xUnit collection name.
    /// </summary>
    public const string Name = "EF Outbox SQL Server";
}
