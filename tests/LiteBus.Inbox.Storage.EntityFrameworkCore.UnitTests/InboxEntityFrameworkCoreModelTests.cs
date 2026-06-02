using LiteBus.Inbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Verifies inbox Entity Framework Core model configuration helpers.
/// </summary>
public sealed class InboxEntityFrameworkCoreModelTests
{
    [Fact]
    public void GetModelBuilderConfiguration_ShouldUsePostgreSqlCanonicalDefaults()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration();

        var entity = modelBuilder.Model.FindEntityType(typeof(InboxMessageEntity));
        entity.Should().NotBeNull();
        entity!.GetSchema().Should().Be("public");
        entity.GetTableName().Should().Be("litebus_inbox_commands");
    }

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

    [Fact]
    public void GetModelBuilderConfiguration_ShouldMapTraceContextAsJsonbWhenProviderSpecified()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration(provider: EfCoreStorageProvider.PostgreSql);

        var entity = modelBuilder.Model.FindEntityType(typeof(InboxMessageEntity));
        var traceContext = entity!.FindProperty(nameof(InboxMessageEntity.TraceContext));

        traceContext.Should().NotBeNull();
        traceContext!.GetColumnName().Should().Be("trace_context");
        traceContext.GetColumnType().Should().Be("jsonb");
        traceContext.IsNullable.Should().BeTrue();
    }

    [Fact]
    public void GetModelBuilderConfiguration_ShouldLeaveTraceContextProviderNeutralByDefault()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration();

        var entity = modelBuilder.Model.FindEntityType(typeof(InboxMessageEntity));
        var traceContext = entity!.FindProperty(nameof(InboxMessageEntity.TraceContext));

        traceContext.Should().NotBeNull();
        traceContext!.GetColumnType().Should().BeNull();
    }
}
