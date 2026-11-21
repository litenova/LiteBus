# PostgreSQL Audit Storage

`LiteBus.Auditing.Storage.PostgreSql` stores audit records as PostgreSQL rows. `PostgreSqlAuditTrail<TDocument>` stages a record for a successful mediation in the ambient business transaction and records for denials, failures, cancellations, and invalid messages on their own connection, and `PostgreSqlAuditChainStore<TDocument>` implements the `IAuditChainStore<TDocument>` port the chain engine runs on. Use it when the application owns a PostgreSQL database and Npgsql connection management.

For what an audit record contains and how capture and linking work, see [Auditing](../concepts/auditing.md). For shared PostgreSQL schema helpers, see [PostgreSQL Schema Management](postgresql-schema-management.md).

## Install

Reference `LiteBus.Auditing` and `LiteBus.Auditing.Storage.PostgreSql`. The package resolves Npgsql through `LiteBus.Storage.PostgreSql`.

## Register

```csharp
services.AddLiteBus(registry =>
{
    registry.AddAuditing(auditing => auditing
        .Capture(AuditCapture.WithChange())
        .UsePostgreSqlStore(postgres => postgres
            .UseConnectionString(configuration.GetConnectionString("OrdersDb")!))
        .UseHashChain(chain => chain
            .CheckpointWhen(CheckpointPolicies.EveryRecords(100))));
});
```

`UsePostgreSqlStore` records the open-generic trail and chain store the auditing module closes over the configured document type, with `PostgreSqlAuditRecord` mapped by `PostgreSqlAuditRecordMapper` and segmented by `PostgreSqlAuditRecordSegmenter` unless the chain configuration names its own, and registers `PostgreSqlAuditModule` as a child of the auditing module carrying the data source and the table names: `UseConnectionString` builds a data source the module owns and disposes, `UseDataSource` takes one the application owns (pass one shared data source to every LiteBus PostgreSQL store on the same database), `UseOptions` replaces the default `PostgreSqlAuditStoreOptions`, and `DisableSchemaInitialization` leaves the tables to a migration pipeline. Registering the module without a data source throws at build. Register one audit store per composition: registering a second store throws `LiteBusConfigurationException`.

## Audit Trail

A record for a successful mediation is staged through the ambient `IPostgreSqlTransactionProvider` transaction when one is active, so the record commits or rolls back with the handler change. The chain linker assigns the staged record its position after commit, in commit order. Register an `IPostgreSqlTransactionProvider` when the business data lives on the same PostgreSQL transaction; without one, every record commits on its own connection.

A record for a denial, failure, cancellation, or invalid message cannot ride that transaction, because the unit of work rolls it back on exactly those paths. Those records are staged out of band on their own connection and committed immediately, so a refusal or a fault is still recorded when the change it describes never commits, and the linker still positions them.

## Schema

The audit store keeps three tables in one schema:

| Setting | Default |
| --- | --- |
| Schema | `public` |
| Records table | `litebus_audit_records` |
| Heads table | `litebus_audit_heads` |
| Checkpoints table | `litebus_audit_checkpoints` |

The records table holds staged and linked documents, the heads table holds one row per chain segment with the conditional-write version, and the checkpoints table holds stored checkpoints with their publish state. The audit schema uses version 1. `PostgreSqlAuditSchema.EnsureAsync` creates the schema idempotently under a PostgreSQL advisory lock, so several application instances can run it at once, and `ValidateAsync` fails startup with `PostgreSqlSchemaDriftException` when a table is missing, incomplete, or recorded at an unexpected version. `EnsureSchemaCreationOnStartup` and `ValidateSchemaCreationOnStartup` are both `true` by default; production systems that use Flyway, Liquibase, or DBA-owned migrations set the first to `false` and apply the canonical SQL files from `PostgreSqlAuditSchema.SqlFiles` or the script from `PostgreSqlAuditSchema.GetCreateScript`. The `audit.postgresql.schema` probe reports the schema state.

## Chain Defaults

With no chain configuration beyond `UseHashChain`, the chain stores `PostgreSqlAuditRecord` rows, segments them by `{category}/{year}` (with `uncategorized` for a record that carries no category), hashes them with the `sha256-v2` scheme, and cuts a checkpoint every 100 records. The default segmenter describes each segment with the record tenant and the `category` and `year` attributes, so listing, expiry, and erasure select segments without parsing keys. Segments open without a genesis document.

Resolve `IAuditChain` to operate on segments: `VerifyAsync` and `VerifyFromAsync` verify, `CutCheckpointAsync` cuts a checkpoint now, `ListSegmentsAsync` lists by filter, and `ExpireSegmentAsync` and `EraseAsync` delete. See [Auditing](../concepts/auditing.md#verifying-a-segment) and [Auditing](../concepts/auditing.md#segments-expiry-and-erasure).

## Limitations

Success audit records share the ambient transaction and roll back with it. Without a registered transaction provider, every record commits on its own connection instead, and composition reports the effective capture it selected.

Hash-chained appends serialize per segment through the segment head row. The chain suits a compliance trail written at command volume, not a hot path that stages thousands of records per second into one segment.

## Related Docs

- [Auditing](../concepts/auditing.md)
- [PostgreSQL Schema Management](postgresql-schema-management.md)
- [Marten Audit Storage](auditing-marten.md)
- [Entity Framework Core Audit Storage](auditing-entity-framework-core.md)
