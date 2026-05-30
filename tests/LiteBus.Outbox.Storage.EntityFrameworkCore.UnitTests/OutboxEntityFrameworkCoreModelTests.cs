using LiteBus.Outbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Unit tests for outbox Entity Framework Core model configuration.
/// </summary>
public sealed class OutboxEntityFrameworkCoreModelTests
{
    [Fact]
    public void GetModelBuilderConfiguration_ShouldMapEntityColumns()
    {
        var options = new EfCoreOutboxStoreOptions
        {
            SchemaName = "app",
            TableName = "outbox"
        };

        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration(options);
        var entity = modelBuilder.Model.FindEntityType(typeof(OutboxMessageEntity));

        entity.Should().NotBeNull();
        entity!.GetTableName().Should().Be("outbox");
        entity.GetSchema().Should().Be("app");
    }
}
