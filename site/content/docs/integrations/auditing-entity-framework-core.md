# Entity Framework Core Audit Storage

`LiteBus.Auditing.Storage.EntityFrameworkCore` stores audit records as Entity Framework Core entities. `EfCoreAuditTrail<TDocument>` stages a record for a successful mediation on the scoped application context and records for denials, failures, cancellations, and invalid messages on an isolated context of their own, and `EfCoreAuditChainStore<TDocument>` implements the `IAuditChainStore<TDocument>` port the chain engine runs on. Use it when the application already owns a `DbContext` and EF migrations.

For what an audit record contains and how capture and linking work, see [Auditing](../concepts/auditing.md).

## Schema Ownership

LiteBus does not create or upgrade audit tables for EF Core. Your application owns migrations. Apply `AuditEntityFrameworkCoreModelExtensions.GetModelBuilderConfiguration()` in `OnModelCreating` so columns and indexes match `EfCoreAuditChainStore<TDocument>`, and generate an application-owned migration from the complete current model:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.GetModelBuilderConfiguration(
        new EntityFrameworkCoreAuditStoreOptions(),
        EfCoreStorageProvider.PostgreSql);
}
```

`EntityFrameworkCoreAuditStoreOptions` names the schema (`public` by default) and the three tables: `litebus_audit_records`, `litebus_audit_segment_heads`, and `litebus_audit_checkpoints`. Pass `EfCoreStorageProvider` so payload and timestamp columns use the correct store types. The generic overload maps a custom document type instead of `EfCoreAuditRecord`; the application then owns the document table name and primary key, and LiteBus configures only the chain columns, the staged-at shadow property the store orders unlinked documents by, and the segment and sequence index. A custom document must not declare its own `StagedAt` property, because the shadow property collides with it and model building fails.

## Register

`AppDbContext` must derive from `DbContext` and implement `IAuditDbContext`, which exposes `DbSet<AuditSegmentHeadEntity> AuditSegmentHeads` and `DbSet<AuditCheckpointEntity> AuditCheckpoints`. Stored documents need no `DbSet` property: the model configuration adds them. The context type parameter on `UseEntityFrameworkCoreStore<TContext>` proves at compile time that the application context joins audit storage:

```csharp
services.AddLiteBus(registry => registry.AddAuditing(auditing => auditing
    .Capture(AuditCapture.WithChange())
    .UseEntityFrameworkCoreStore<AppDbContext>()
    .UseHashChain(chain => chain
        .CheckpointWhen(CheckpointPolicies.EveryRecords(100)))));
```

`UseEntityFrameworkCoreStore<TContext>` records the open-generic trail and chain store the auditing module closes over the configured document type, with `EfCoreAuditRecord` mapped by `EfCoreAuditRecordMapper` and segmented by `EfCoreAuditRecordSegmenter` unless the chain configuration names its own. Register one audit store per composition: registering a second store throws `LiteBusConfigurationException`. LiteBus registers neither the application context nor the factory below; the application registers both.

## Audit Trail

A record for a successful mediation is staged on the scoped context and committed by the same `SaveChanges` that commits the handler change, so the record and the change are atomic: both commit together and both roll back together. The chain linker assigns the staged record its position after commit, in commit order.

A record for a denial, failure, cancellation, or invalid message cannot ride that transaction, because the unit of work rolls the scoped context back on exactly those paths. Those records are staged out of band on an isolated context and saved immediately, so a refusal or a fault is still recorded when the change it describes never commits, and the linker still positions them. Register `EfCoreAuditDbContextFactory<TContext>` behind an Entity Framework context factory so the isolated contexts can be opened:

```csharp
services.AddDbContextFactory<AppDbContext>(options => options.UseNpgsql(connectionString));
services.AddSingleton<IAuditDbContextFactory, EfCoreAuditDbContextFactory<AppDbContext>>();
```

The store creates and disposes one isolated context per out-of-band record.

## Chain Defaults

With no chain configuration beyond `UseHashChain`, the chain stores `EfCoreAuditRecord` entities, segments them by `{category}/{year}` (with `uncategorized` for a record that carries no category), hashes them with the `sha256-v2` scheme, and cuts a checkpoint every 100 records. The default segmenter describes each segment with the record tenant and the `category` and `year` attributes, so listing, expiry, and erasure select segments without parsing keys. Segments open without a genesis document.

Resolve `IAuditChain` to operate on segments: `VerifyAsync` and `VerifyFromAsync` verify, `CutCheckpointAsync` cuts a checkpoint now, `ListSegmentsAsync` lists by filter, and `ExpireSegmentAsync` and `EraseAsync` delete. See [Auditing](../concepts/auditing.md#verifying-a-segment) and [Auditing](../concepts/auditing.md#segments-expiry-and-erasure).

## Limitations

Success audit records share the scoped context and roll back with it. Without a registered `IAuditDbContextFactory`, out-of-band records cannot be written.

Hash-chained appends serialize per segment through the segment head row version. The chain suits a compliance trail written at command volume, not a hot path that stages thousands of records per second into one segment.

## Related Docs

- [Auditing](../concepts/auditing.md)
- [Marten Audit Storage](auditing-marten.md)
- [PostgreSQL Audit Storage](auditing-postgresql.md)
- [Outbox Entity Framework Core Storage](outbox-ef-core-storage.md)
