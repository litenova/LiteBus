using System;
using LiteBus.Auditing.Abstractions;
using System.Linq.Expressions;
using LiteBus.Messaging.Abstractions.Auditing;
using LiteBus.Storage.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LiteBus.Auditing.Storage.EntityFrameworkCore;

/// <summary>
///     Fluent API helpers for mapping audit tables in application migrations.
/// </summary>
/// <remarks>
///     <para>
///         The application calls one overload from <c>OnModelCreating</c>: the parameterless document overload
///         maps the default <see cref="EfCoreAuditRecord" /> document, and the generic overload maps a custom
///         document type instead. Both overloads map the segment head and checkpoint tables.
///     </para>
/// </remarks>
public static class AuditEntityFrameworkCoreModelExtensions
{
    /// <summary>
    ///     Applies audit table mapping, indexes, and column constraints to the model builder.
    /// </summary>
    /// <param name="modelBuilder">The model builder used by the application <see cref="DbContext" />.</param>
    /// <param name="options">Optional store options that control schema and table names.</param>
    /// <param name="provider">
    ///     An optional storage provider used to apply store-specific column types. When omitted, payload and
    ///     timestamp columns remain provider-neutral strings and applications should set column types explicitly
    ///     if needed.
    /// </param>
    /// <returns>The same <paramref name="modelBuilder" /> for chaining.</returns>
    public static ModelBuilder GetModelBuilderConfiguration(
        this ModelBuilder modelBuilder,
        EntityFrameworkCoreAuditStoreOptions? options = null,
        EfCoreStorageProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        options ??= new EntityFrameworkCoreAuditStoreOptions();
        ConfigureSegmentHeadEntity(modelBuilder.Entity<AuditSegmentHeadEntity>(), options, provider);
        ConfigureCheckpointEntity(modelBuilder.Entity<AuditCheckpointEntity>(), options, provider);
        ConfigureDefaultRecordEntity(modelBuilder.Entity<EfCoreAuditRecord>(), options, provider);
        return modelBuilder;
    }

    /// <summary>
    ///     Applies audit table mapping for a custom document type, with indexes and column constraints.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type the application maps.</typeparam>
    /// <param name="modelBuilder">The model builder used by the application <see cref="DbContext" />.</param>
    /// <param name="options">Optional store options that control schema and table names.</param>
    /// <param name="provider">
    ///     An optional storage provider used to apply store-specific column types. When omitted, payload and
    ///     timestamp columns remain provider-neutral strings and applications should set column types explicitly
    ///     if needed.
    /// </param>
    /// <returns>The same <paramref name="modelBuilder" /> for chaining.</returns>
    /// <remarks>
    ///     <para>
    ///         The application owns the document table name and primary key: call this overload, then map the
    ///         table and key on the same entity. LiteBus configures only the chain columns, the staged-at
    ///         shadow property the store orders unlinked documents by, and the segment and sequence index. A
    ///         custom document must not declare its own <c>StagedAt</c> property, because the shadow property
    ///         collides with it and model building fails.
    ///     </para>
    /// </remarks>
    public static ModelBuilder GetModelBuilderConfiguration<TDocument>(
        this ModelBuilder modelBuilder,
        EntityFrameworkCoreAuditStoreOptions? options = null,
        EfCoreStorageProvider? provider = null)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        options ??= new EntityFrameworkCoreAuditStoreOptions();
        ConfigureSegmentHeadEntity(modelBuilder.Entity<AuditSegmentHeadEntity>(), options, provider);
        ConfigureCheckpointEntity(modelBuilder.Entity<AuditCheckpointEntity>(), options, provider);
        ConfigureDocumentChain(modelBuilder.Entity<TDocument>(), provider);
        return modelBuilder;
    }

    /// <summary>
    ///     Configures one <see cref="AuditSegmentHeadEntity" /> type for audit persistence.
    /// </summary>
    /// <param name="entity">The entity type builder.</param>
    /// <param name="options">Store options that control schema and table names.</param>
    /// <param name="provider">An optional storage provider used to apply store-specific column types.</param>
    internal static void ConfigureSegmentHeadEntity(
        EntityTypeBuilder<AuditSegmentHeadEntity> entity,
        EntityFrameworkCoreAuditStoreOptions options,
        EfCoreStorageProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);

        if (provider is EfCoreStorageProvider.MySql or EfCoreStorageProvider.Sqlite)
        {
            entity.ToTable(options.SegmentHeadsTableName);
        }
        else
        {
            entity.ToTable(options.SegmentHeadsTableName, options.SchemaName);
        }

        entity.HasKey(head => head.Segment);

        entity.Property(head => head.Segment)
            .HasColumnName("segment")
            .HasMaxLength(450)
            .IsRequired();

        entity.Property(head => head.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(256);

        entity.Property(head => head.AttributesJson)
            .HasColumnName("attributes_json")
            .ConfigureJsonPayloadColumn<AuditSegmentHeadEntity>(provider)
            .IsRequired();

        entity.Property(head => head.OpenedAt)
            .HasColumnName("opened_at")
            .ConfigureUtcTimestampColumn(provider);

        entity.Property(head => head.LastSequence)
            .HasColumnName("last_sequence");

        entity.Property(head => head.LastHash)
            .HasColumnName("last_hash")
            .HasMaxLength(256);

        entity.Property(head => head.Count)
            .HasColumnName("count");

        entity.Property(head => head.LastCheckpointSequence)
            .HasColumnName("last_checkpoint_sequence");

        entity.Property(head => head.LastCheckpointAt)
            .HasColumnName("last_checkpoint_at")
            .ConfigureUtcTimestampColumn(provider);

        entity.Property(head => head.Expired)
            .HasColumnName("expired");

        entity.Property(head => head.Version)
            .HasColumnName("version")
            .IsConcurrencyToken();

        entity.HasIndex(head => new { head.TenantId, head.Expired, head.OpenedAt });
    }

    /// <summary>
    ///     Configures one <see cref="AuditCheckpointEntity" /> type for audit persistence.
    /// </summary>
    /// <param name="entity">The entity type builder.</param>
    /// <param name="options">Store options that control schema and table names.</param>
    /// <param name="provider">An optional storage provider used to apply store-specific column types.</param>
    internal static void ConfigureCheckpointEntity(
        EntityTypeBuilder<AuditCheckpointEntity> entity,
        EntityFrameworkCoreAuditStoreOptions options,
        EfCoreStorageProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);

        if (provider is EfCoreStorageProvider.MySql or EfCoreStorageProvider.Sqlite)
        {
            entity.ToTable(options.CheckpointsTableName);
        }
        else
        {
            entity.ToTable(options.CheckpointsTableName, options.SchemaName);
        }

        entity.HasKey(checkpoint => checkpoint.Id);

        entity.Property(checkpoint => checkpoint.Id)
            .HasColumnName("checkpoint_id")
            .ValueGeneratedNever();

        entity.Property(checkpoint => checkpoint.Segment)
            .HasColumnName("segment")
            .HasMaxLength(450)
            .IsRequired();

        entity.Property(checkpoint => checkpoint.Sequence)
            .HasColumnName("sequence");

        entity.Property(checkpoint => checkpoint.Count)
            .HasColumnName("count");

        entity.Property(checkpoint => checkpoint.Hash)
            .HasColumnName("hash")
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(checkpoint => checkpoint.HashScheme)
            .HasColumnName("hash_scheme")
            .HasMaxLength(100)
            .IsRequired();

        entity.Property(checkpoint => checkpoint.Kind)
            .HasColumnName("kind")
            .HasConversion<int>();

        entity.Property(checkpoint => checkpoint.CreatedAt)
            .HasColumnName("created_at")
            .ConfigureUtcTimestampColumn(provider);

        entity.Property(checkpoint => checkpoint.Label)
            .HasColumnName("label")
            .HasMaxLength(256);

        entity.Property(checkpoint => checkpoint.SignatureScheme)
            .HasColumnName("signature_scheme")
            .HasMaxLength(256);

        entity.Property(checkpoint => checkpoint.SignatureValue)
            .HasColumnName("signature_value")
            .ConfigureNullablePayloadColumn(provider);

        entity.Property(checkpoint => checkpoint.SignaturesJson)
            .HasColumnName("signatures_json")
            .ConfigureJsonPayloadColumn<AuditCheckpointEntity>(provider)
            .IsRequired();

        entity.Property(checkpoint => checkpoint.PublishState)
            .HasColumnName("publish_state")
            .HasConversion<int>();

        entity.Property(checkpoint => checkpoint.PublishAttempts)
            .HasColumnName("publish_attempts");

        entity.Property(checkpoint => checkpoint.LastPublishError)
            .HasColumnName("last_publish_error")
            .ConfigureNullablePayloadColumn(provider);

        entity.HasIndex(checkpoint => new { checkpoint.Segment, checkpoint.Sequence });
        entity.HasIndex(checkpoint => new { checkpoint.PublishState, checkpoint.CreatedAt });
    }

    /// <summary>
    ///     Configures the default <see cref="EfCoreAuditRecord" /> document for audit persistence.
    /// </summary>
    /// <param name="entity">The entity type builder.</param>
    /// <param name="options">Store options that control schema and table names.</param>
    /// <param name="provider">An optional storage provider used to apply store-specific column types.</param>
    internal static void ConfigureDefaultRecordEntity(
        EntityTypeBuilder<EfCoreAuditRecord> entity,
        EntityFrameworkCoreAuditStoreOptions options,
        EfCoreStorageProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(options);

        if (provider is EfCoreStorageProvider.MySql or EfCoreStorageProvider.Sqlite)
        {
            entity.ToTable(options.AuditRecordsTableName);
        }
        else
        {
            entity.ToTable(options.AuditRecordsTableName, options.SchemaName);
        }

        entity.HasKey(document => document.Id);

        entity.Property(document => document.Id)
            .HasColumnName("record_id")
            .ValueGeneratedNever();

        entity.Property(document => document.Action)
            .HasColumnName("action")
            .HasMaxLength(256)
            .IsRequired();

        entity.Property(document => document.ActorId)
            .HasColumnName("actor_id")
            .HasMaxLength(256);

        entity.Property(document => document.ActorKind)
            .HasColumnName("actor_kind")
            .HasMaxLength(64);

        entity.Property(document => document.ActorDisplayName)
            .HasColumnName("actor_display_name")
            .HasMaxLength(256);

        entity.Property(document => document.ActorOnBehalfOf)
            .HasColumnName("actor_on_behalf_of")
            .HasMaxLength(256);

        entity.Property(document => document.Outcome)
            .HasColumnName("outcome")
            .HasConversion<int>();

        entity.Property(document => document.OccurredAt)
            .HasColumnName("occurred_at")
            .ConfigureUtcTimestampColumn(provider);

        entity.Property(document => document.Duration)
            .HasColumnName("duration");

        entity.Property(document => document.Category)
            .HasColumnName("category")
            .HasMaxLength(128);

        entity.Property(document => document.TargetKind)
            .HasColumnName("target_kind")
            .HasMaxLength(128);

        entity.Property(document => document.TargetId)
            .HasColumnName("target_id")
            .HasMaxLength(256);

        entity.Property(document => document.Reason)
            .HasColumnName("reason")
            .ConfigureNullablePayloadColumn(provider);

        entity.Property(document => document.FailureCode)
            .HasColumnName("failure_code")
            .HasMaxLength(128);

        entity.Property(document => document.MessageType)
            .HasColumnName("message_type")
            .HasMaxLength(512);

        entity.Property(document => document.CorrelationId)
            .HasColumnName("correlation_id")
            .HasMaxLength(128);

        entity.Property(document => document.TenantId)
            .HasColumnName("tenant_id")
            .HasMaxLength(256);

        entity.Property(document => document.PropertiesJson)
            .HasColumnName("properties_json")
            .ConfigureJsonPayloadColumn<EfCoreAuditRecord>(provider)
            .IsRequired();

        entity.Property(document => document.Segment)
            .HasColumnName("segment")
            .HasMaxLength(450)
            .IsRequired();

        entity.Property(document => document.Sequence)
            .HasColumnName("sequence");

        entity.Property(document => document.PreviousHash)
            .HasColumnName("previous_hash")
            .HasMaxLength(256);

        entity.Property(document => document.Hash)
            .HasColumnName("hash")
            .HasMaxLength(256);

        entity.Property(document => document.HashScheme)
            .HasColumnName("hash_scheme")
            .HasMaxLength(100);

        entity.Property(document => document.StagedAt)
            .HasColumnName("staged_at")
            .ConfigureUtcTimestampColumn(provider);

        ConfigureDocumentIndex(entity, provider);
    }

    /// <summary>
    ///     Configures the chain columns, staged-at shadow property, and segment index of a custom document.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type the application maps.</typeparam>
    /// <param name="entity">The entity type builder.</param>
    /// <param name="provider">An optional storage provider used to apply store-specific column types.</param>
    internal static void ConfigureDocumentChain<TDocument>(
        EntityTypeBuilder<TDocument> entity,
        EfCoreStorageProvider? provider = null)
        where TDocument : class, IAuditChainDocument
    {
        ArgumentNullException.ThrowIfNull(entity);

        entity.Property(document => document.Segment)
            .HasColumnName("segment")
            .HasMaxLength(450)
            .IsRequired();

        entity.Property(document => document.Sequence)
            .HasColumnName("sequence");

        entity.Property(document => document.PreviousHash)
            .HasColumnName("previous_hash")
            .HasMaxLength(256);

        entity.Property(document => document.Hash)
            .HasColumnName("hash")
            .HasMaxLength(256);

        entity.Property(document => document.HashScheme)
            .HasColumnName("hash_scheme")
            .HasMaxLength(100);

        entity.Property<DateTimeOffset>(AuditStoreJson.StagedAtPropertyName)
            .HasColumnName("staged_at")
            .ConfigureUtcTimestampColumn(provider);

        ConfigureDocumentIndex(
            entity,
            document => new
            {
                document.Segment,
                document.Sequence,
                StagedAt = EF.Property<DateTimeOffset>(document, AuditStoreJson.StagedAtPropertyName),
            },
            provider);
    }

    /// <summary>
    ///     Configures an optional text property with the provider payload column type when a provider is
    ///     supplied.
    /// </summary>
    /// <param name="property">The property builder.</param>
    /// <param name="provider">
    ///     The target provider; when <see langword="null" />, no store-specific column type is applied.
    /// </param>
    /// <returns>The same property builder for chaining.</returns>
    /// <remarks>
    ///     The shared payload helper covers required strings only, while reasons, signature values, and
    ///     publish errors are optional text on every provider: unbounded Unicode text rather than a JSON
    ///     type, because their content is not JSON.
    /// </remarks>
    private static PropertyBuilder<string?> ConfigureNullablePayloadColumn(
        this PropertyBuilder<string?> property,
        EfCoreStorageProvider? provider)
    {
        ArgumentNullException.ThrowIfNull(property);

        if (provider is null)
        {
            return property;
        }

        return property.HasColumnType(EfCoreRelationalModelColumnTypes.GetPayloadColumnType(provider.Value));
    }

    /// <summary>
    ///     Adds the segment, sequence, and staged-at index of the default record table.
    /// </summary>
    /// <param name="entity">The entity type builder.</param>
    /// <param name="provider">
    ///     An optional storage provider used to choose a filtered unique index where the provider supports
    ///     one.
    /// </param>
    /// <remarks>
    ///     <para>
    ///         The index serves both document reads: linked reads filter by segment and sequence, and unlinked
    ///         reads filter by segment with sequence zero in staged-at order. Unlinked documents all carry
    ///         sequence zero, so the uniqueness filter covers linked documents only.
    ///     </para>
    ///     <para>
    ///         MySQL supports no filtered index and the in-memory provider enforces no index, so those two map
    ///         a plain index and the head version check remains the guard that serializes appends.
    ///     </para>
    /// </remarks>
    private static void ConfigureDocumentIndex(
        EntityTypeBuilder<EfCoreAuditRecord> entity,
        EfCoreStorageProvider? provider)
    {
        if (provider is null or EfCoreStorageProvider.PostgreSql or EfCoreStorageProvider.SqlServer or EfCoreStorageProvider.Sqlite)
        {
            entity.HasIndex(document => new { document.Segment, document.Sequence, document.StagedAt })
                .IsUnique()
                .HasFilter("sequence > 0");
            return;
        }

        entity.HasIndex(document => new { document.Segment, document.Sequence, document.StagedAt });
    }

    /// <summary>
    ///     Adds the segment, sequence, and staged-at index of a custom document table.
    /// </summary>
    /// <typeparam name="TDocument">The stored audit document type the application maps.</typeparam>
    /// <param name="entity">The entity type builder.</param>
    /// <param name="index">
    ///     The index expression over the segment, sequence, and staged-at shadow property.
    /// </param>
    /// <param name="provider">
    ///     An optional storage provider used to choose a filtered unique index where the provider supports
    ///     one.
    /// </param>
    private static void ConfigureDocumentIndex<TDocument>(
        EntityTypeBuilder<TDocument> entity,
        Expression<Func<TDocument, object?>> index,
        EfCoreStorageProvider? provider)
        where TDocument : class, IAuditChainDocument
    {
        if (provider is null or EfCoreStorageProvider.PostgreSql or EfCoreStorageProvider.SqlServer or EfCoreStorageProvider.Sqlite)
        {
            entity.HasIndex(index)
                .IsUnique()
                .HasFilter("sequence > 0");
            return;
        }

        entity.HasIndex(index);
    }
}
