using System;
using LiteBus.Outbox.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiteBus.Outbox.Storage.EntityFrameworkCore;

/// <summary>
///     Fluent API helpers for mapping <see cref="OutboxMessageEntity" /> in application migrations.
/// </summary>
public static class OutboxEntityFrameworkCoreModelExtensions
{
    /// <summary>
    ///     Applies outbox table mapping, indexes, and column constraints to the model builder.
    /// </summary>
    /// <param name="modelBuilder">The model builder used by the application <see cref="DbContext" />.</param>
    /// <param name="options">Optional store options that control schema and table names.</param>
    /// <returns>The same <paramref name="modelBuilder" /> for chaining.</returns>
    public static ModelBuilder GetModelBuilderConfiguration(
        this ModelBuilder modelBuilder,
        EfCoreOutboxStoreOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        options ??= new EfCoreOutboxStoreOptions();
        ConfigureOutboxMessageEntity(modelBuilder.Entity<OutboxMessageEntity>(), options);
        return modelBuilder;
    }

    /// <summary>
    ///     Configures one <see cref="OutboxMessageEntity" /> type for outbox persistence.
    /// </summary>
    /// <param name="entity">The entity type builder.</param>
    /// <param name="options">Store options that control schema and table names.</param>
    internal static void ConfigureOutboxMessageEntity(
        EntityTypeBuilder<OutboxMessageEntity> entity,
        EfCoreOutboxStoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);

        entity.ToTable(options.TableName, options.SchemaName);
        entity.HasKey(message => message.Id);

        entity.Property(message => message.Id)
            .HasColumnName("message_id");

        entity.Property(message => message.ContractName)
            .HasColumnName("contract_name")
            .IsRequired();

        entity.Property(message => message.ContractVersion)
            .HasColumnName("contract_version");

        entity.Property(message => message.Payload)
            .HasColumnName("payload")
            .IsRequired();

        entity.Property(message => message.Topic)
            .HasColumnName("topic");

        entity.Property(message => message.CreatedAt)
            .HasColumnName("created_at");

        entity.Property(message => message.VisibleAfter)
            .HasColumnName("visible_after");

        entity.Property(message => message.Status)
            .HasColumnName("status")
            .HasConversion<int>();

        entity.Property(message => message.AttemptCount)
            .HasColumnName("attempt_count");

        entity.Property(message => message.LeaseOwner)
            .HasColumnName("lease_owner");

        entity.Property(message => message.LeaseExpiresAt)
            .HasColumnName("lease_expires_at");

        entity.Property(message => message.LastError)
            .HasColumnName("last_error");

        entity.Property(message => message.CorrelationId)
            .HasColumnName("correlation_id");

        entity.Property(message => message.CausationId)
            .HasColumnName("causation_id");

        entity.Property(message => message.TenantId)
            .HasColumnName("tenant_id");

        entity.HasIndex(message => new { message.Status, message.VisibleAfter, message.LeaseExpiresAt, message.CreatedAt });

        entity.HasIndex(message => message.Topic)
            .HasFilter("topic IS NOT NULL");
    }
}
