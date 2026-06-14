using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using LiteBus.Inbox.Storage.EntityFrameworkCore;

namespace LiteBus.Storage.UnitTests.EntityFrameworkCore.Inbox;

/// <summary>
///     Verifies inbox Entity Framework Core model configuration helpers.
/// </summary>
public sealed class InboxEntityFrameworkCoreModelTests
{
    [Fact]
    public void GetModelBuilderConfiguration_ShouldMapVersion1Columns()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration();

        var entity = modelBuilder.Model.FindEntityType(typeof(InboxMessageEntity));
        entity.Should().NotBeNull();

        string[] expectedColumns =
        [
            "message_id",
            "contract_name",
            "contract_version",
            "payload",
            "created_at",
            "visible_after",
            "attempt_count",
            "status",
            "idempotency_key",
            "lease_owner",
            "lease_expires_at",
            "last_error",
            "correlation_id",
            "causation_id",
            "tenant_id",
            "trace_context",
            "completed_at",
            "last_attempted_at",
            "first_failed_at",
            "dead_lettered_at",
            "last_lease_owner",
            "error_type"
        ];

        var mappedColumns = entity.GetProperties().Select(property => property.GetColumnName());
        mappedColumns.Should().BeEquivalentTo(expectedColumns);
    }

    [Fact]
    public void GetModelBuilderConfiguration_ShouldMapTenantScopedIdempotencyIndex()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration();

        var entity = modelBuilder.Model.FindEntityType(typeof(InboxMessageEntity));
        entity.Should().NotBeNull();

        var hasTenantScopedIdempotencyIndex = entity.GetIndexes().Any(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(
                [nameof(InboxMessageEntity.TenantId), nameof(InboxMessageEntity.IdempotencyKey)]) &&
            index.IsUnique &&
            index.GetFilter() == "idempotency_key IS NOT NULL");

        hasTenantScopedIdempotencyIndex.Should().BeTrue();
    }

    [Fact]
    public void GetModelBuilderConfiguration_ShouldUsePostgreSqlCanonicalDefaults()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration();

        var entity = modelBuilder.Model.FindEntityType(typeof(InboxMessageEntity));
        entity.Should().NotBeNull();
        entity.GetSchema().Should().Be("public");
        entity.GetTableName().Should().Be("litebus_inbox_messages");
    }

    [Fact]
    public void GetModelBuilderConfiguration_ShouldMapCustomSchemaAndTable()
    {
        var options = new EntityFrameworkCoreInboxStoreOptions
        {
            SchemaName = "app",
            TableName = "inbox_messages"
        };

        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration(options);

        var entity = modelBuilder.Model.FindEntityType(typeof(InboxMessageEntity));
        entity.Should().NotBeNull();
        entity.GetSchema().Should().Be("app");
        entity.GetTableName().Should().Be("inbox_messages");
        entity.FindProperty(nameof(InboxMessageEntity.Id))!.GetColumnName().Should().Be("message_id");
    }

    [Fact]
    public void GetModelBuilderConfiguration_ShouldMapTraceContextAsJsonbWhenProviderSpecified()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration(provider: EfCoreStorageProvider.PostgreSql);

        var entity = modelBuilder.Model.FindEntityType(typeof(InboxMessageEntity));
        var traceContext = entity!.FindProperty(nameof(InboxMessageEntity.TraceContext));

        traceContext.Should().NotBeNull();
        traceContext.GetColumnName().Should().Be("trace_context");
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
        traceContext.GetColumnType().Should().BeNull();
    }
}
