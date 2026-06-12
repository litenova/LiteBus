using System;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiteBus.Inbox.Storage.EntityFrameworkCore;

/// <summary>
///     Fluent API helpers for mapping <see cref="InboxMessageEntity" /> in application migrations.
/// </summary>
public static class InboxEntityFrameworkCoreModelExtensions
{
    /// <summary>
    ///     Applies inbox table mapping, indexes, and column constraints to the model builder.
    /// </summary>
    /// <param name="modelBuilder">The model builder used by the application <see cref="DbContext" />.</param>
    /// <param name="options">Optional store options that control schema and table names.</param>
    /// <param name="provider">
    ///     An optional storage provider used to apply store-specific JSON column types. When omitted, payload and
    ///     trace columns remain provider-neutral strings and applications should set column types explicitly if needed.
    /// </param>
    /// <returns>The same <paramref name="modelBuilder" /> for chaining.</returns>
    public static ModelBuilder GetModelBuilderConfiguration(
        this ModelBuilder modelBuilder,
        EntityFrameworkCoreInboxStoreOptions? options = null,
        EfCoreStorageProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        options ??= new EntityFrameworkCoreInboxStoreOptions();
        ConfigureInboxMessageEntity(modelBuilder.Entity<InboxMessageEntity>(), options, provider);
        return modelBuilder;
    }

    /// <summary>
    ///     Configures one <see cref="InboxMessageEntity" /> type for inbox persistence.
    /// </summary>
    /// <param name="entity">The entity type builder.</param>
    /// <param name="options">Store options that control schema and table names.</param>
    /// <param name="provider">
    ///     An optional storage provider used to apply store-specific JSON column types.
    /// </param>
    internal static void ConfigureInboxMessageEntity(
        EntityTypeBuilder<InboxMessageEntity> entity,
        EntityFrameworkCoreInboxStoreOptions options,
        EfCoreStorageProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);

        entity.ToTable(options.TableName, options.SchemaName);

        entity.HasKey(message => message.Id);

        entity.Property(message => message.Id)
            .HasColumnName("message_id")
            .ValueGeneratedNever();

        entity.Property(message => message.ContractName)
            .HasColumnName("contract_name")
            .IsRequired();

        entity.Property(message => message.ContractVersion)
            .HasColumnName("contract_version");

        entity.Property(message => message.Payload)
            .HasColumnName("payload")
            .ConfigureJsonPayloadColumn<InboxMessageEntity>(provider)
            .IsRequired();

        entity.Property(message => message.CreatedAt)
            .HasColumnName("created_at");

        entity.Property(message => message.VisibleAfter)
            .HasColumnName("visible_after");

        entity.Property(message => message.AttemptCount)
            .HasColumnName("attempt_count");

        entity.Property(message => message.Status)
            .HasColumnName("status")
            .HasConversion<int>();

        entity.Property(message => message.IdempotencyKey)
            .HasColumnName("idempotency_key");

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

        entity.Property(message => message.TraceContext)
            .HasColumnName("trace_context")
            .ConfigureJsonTraceContextColumn<InboxMessageEntity>(provider);

        entity.Property(message => message.CompletedAt)
            .HasColumnName("completed_at");

        entity.Property(message => message.LastAttemptedAt)
            .HasColumnName("last_attempted_at");

        entity.Property(message => message.FirstFailedAt)
            .HasColumnName("first_failed_at");

        entity.Property(message => message.DeadLetteredAt)
            .HasColumnName("dead_lettered_at");

        entity.Property(message => message.LastLeaseOwner)
            .HasColumnName("last_lease_owner");

        entity.Property(message => message.ErrorType)
            .HasColumnName("error_type");

        entity.HasIndex(message => new { message.TenantId, message.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL");

        entity.HasIndex(message => new { message.Status, message.VisibleAfter, message.LeaseExpiresAt, message.CreatedAt });
    }
}