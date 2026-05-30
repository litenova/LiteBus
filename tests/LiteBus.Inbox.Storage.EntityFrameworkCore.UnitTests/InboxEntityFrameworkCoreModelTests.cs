using LiteBus.Inbox.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Verifies inbox Entity Framework Core model configuration helpers.
/// </summary>
public sealed class InboxEntityFrameworkCoreModelTests
{
    [Fact]
    public void GetModelBuilderConfiguration_ShouldMapCustomSchemaAndTable()
    {
        var options = new EfCoreInboxStoreOptions
        {
            SchemaName = "app",
            TableName = "command_inbox"
        };

        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration(options);

        var entity = modelBuilder.Model.FindEntityType(typeof(InboxMessageEntity));
        entity.Should().NotBeNull();
        entity!.GetSchema().Should().Be("app");
        entity.GetTableName().Should().Be("command_inbox");
        entity.FindProperty(nameof(InboxMessageEntity.Id))!.GetColumnName().Should().Be("command_id");
    }
}
