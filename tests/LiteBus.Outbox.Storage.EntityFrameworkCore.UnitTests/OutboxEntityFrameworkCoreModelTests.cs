using LiteBus.Outbox.Storage.EntityFrameworkCore;
using LiteBus.Storage.EntityFrameworkCore;
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
