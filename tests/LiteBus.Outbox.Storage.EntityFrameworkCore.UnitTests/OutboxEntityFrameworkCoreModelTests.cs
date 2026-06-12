using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore.UnitTests;

/// <summary>
///     Unit tests for outbox Entity Framework Core model configuration.
/// </summary>
public sealed class OutboxEntityFrameworkCoreModelTests
{
    [Fact]
    public void GetModelBuilderConfiguration_ShouldMapVersion1Columns()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration();

        var entity = modelBuilder.Model.FindEntityType(typeof(OutboxMessageEntity));
        entity.Should().NotBeNull();

        string[] expectedColumns =
        [
            "message_id",
            "contract_name",
            "contract_version",
            "payload",
            "topic",
            "created_at",
            "visible_after",
            "status",
            "attempt_count",
            "lease_owner",
            "lease_expires_at",
            "last_error",
            "correlation_id",
            "causation_id",
            "tenant_id",
            "idempotency_key",
            "trace_context",
            "published_at",
            "last_attempted_at",
            "first_failed_at",
            "dead_lettered_at",
            "last_lease_owner",
            "error_type"
        ];

        var mappedColumns = entity!.GetProperties().Select(property => property.GetColumnName());
        mappedColumns.Should().BeEquivalentTo(expectedColumns);
    }

    [Fact]
    public void GetModelBuilderConfiguration_ShouldMapEntityColumns()
    {
        var options = new EntityFrameworkCoreOutboxStoreOptions
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

        var hasUniqueIdempotencyIndex = entity.GetIndexes().Any(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { nameof(OutboxMessageEntity.IdempotencyKey) }) &&
            index.IsUnique);

        hasUniqueIdempotencyIndex.Should().BeTrue();
    }

    [Fact]
    public void GetModelBuilderConfiguration_ShouldMapTraceContextAsJsonbWhenProviderSpecified()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.GetModelBuilderConfiguration(provider: EfCoreStorageProvider.PostgreSql);

        var entity = modelBuilder.Model.FindEntityType(typeof(OutboxMessageEntity));
        var traceContext = entity!.FindProperty(nameof(OutboxMessageEntity.TraceContext));

        traceContext.Should().NotBeNull();
        traceContext!.GetColumnName().Should().Be("trace_context");
        traceContext.GetColumnType().Should().Be("jsonb");
        traceContext.IsNullable.Should().BeTrue();
    }
}