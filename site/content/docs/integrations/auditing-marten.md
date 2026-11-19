# Marten Audit Storage

`LiteBus.Auditing.Storage.Marten` stores audit records as Marten documents. `MartenAuditTrail<TDocument>` stages a record for a successful mediation on the scoped `IDocumentSession` and records for denials, failures, cancellations, and invalid messages on a short session of their own, and `MartenAuditChainStore<TDocument>` implements the `IAuditChainStore<TDocument>` port the chain engine runs on. Use it when command handlers already stage Marten documents and the audit record should commit with the handler change.

For what an audit record contains and how capture and linking work, see [Auditing](../concepts/auditing.md).

## Install

Reference `LiteBus.Auditing` and `LiteBus.Auditing.Storage.Marten`. Your application already references Marten for `AddMarten`. The storage package resolves the application-owned `IDocumentStore` and the scoped `IDocumentSession` from the container.

## Register

```csharp
services.AddMarten(options =>
{
    options.Connection(connectionString);
});

services.AddLiteBus(registry => registry.AddAuditing(auditing => auditing
    .Capture(AuditCapture.WithChange())
    .UseMartenStore()
    .UseHashChain(chain => chain
        .CheckpointWhen(CheckpointPolicies.EveryRecords(100)))));
```

LiteBus does not register Marten. The application calls `AddMarten`, and the trail and the chain store resolve the scoped session and the document store from the current scope. `UseMartenStore` records the open-generic trail and chain store the auditing module closes over the configured document type, with `MartenAuditRecord` mapped by `MartenAuditRecordMapper` and segmented by `MartenAuditRecordSegmenter` unless the chain configuration names its own. Register one audit store per composition: registering a second store throws `LiteBusConfigurationException`.

## Audit Trail

A record for a successful mediation is staged on the scoped session and committed by the same `SaveChangesAsync` that commits the handler change, so the record and the change are atomic: both commit together and both roll back together. The chain linker assigns the staged record its position after commit, in commit order.

A record for a denial, failure, cancellation, or invalid message cannot ride that transaction, because the unit of work rolls the session back on exactly those paths. Those records are staged out of band on a short session and saved immediately, so a refusal or a fault is still recorded when the change it describes never commits, and the linker still positions them.

## Chain Defaults

With no chain configuration beyond `UseHashChain`, the chain stores `MartenAuditRecord` documents, segments them by `{category}/{year}` (with `uncategorized` for a record that carries no category), hashes them with the `sha256-v2` scheme, and cuts a checkpoint every 100 records. The default segmenter describes each segment with the record tenant and the `category` and `year` attributes, so listing, expiry, and erasure select segments without parsing keys. Segments open without a genesis document.

A `MartenAuditSegmentHead` document per segment serializes appends. Its identity is the segment name, and it carries the last sequence number and hash, the document count, the last checkpoint, and the conditional-write version. Every linked append asserts the expected head version; a concurrent append fails with `AuditHeadConflictException` and the linker retries against the winner, so two records never share a sequence number.

Resolve `IAuditChain` to operate on segments: `VerifyAsync` and `VerifyFromAsync` verify, `CutCheckpointAsync` cuts a checkpoint now, `ListSegmentsAsync` lists by filter, and `ExpireSegmentAsync` and `EraseAsync` delete. See [Auditing](../concepts/auditing.md#verifying-a-segment) and [Auditing](../concepts/auditing.md#segments-expiry-and-erasure).

## Storing a Custom Document

An application whose audit model carries fields LiteBus does not know stores its own document type, with its own mapper, segmenter, and canonicalizer:

```csharp
registry.AddAuditing(auditing => auditing
    .UseMartenStore()
    .UseHashChain(chain => chain
        .StoreAs<WorkspaceAuditDocument, WorkspaceAuditMapper>()
        .SegmentBy<WorkspaceAuditSegmenter>()));
```

Register the canonicalizer for the custom document in the application container, because the scheme registry resolves it per document type. An explicit `StoreAs` or `SegmentBy` call always wins over the store defaults, regardless of call order. See [Auditing](../concepts/auditing.md#storing-a-custom-document).

## Limitations

Success audit records share the handler session and roll back with it. Stage them only through the completion-stage writer, which runs before the commit. Marten must target the database the documents live in.

Hash-chained appends serialize per segment through the segment head. The chain suits a compliance trail written at command volume, not a hot path that stages thousands of records per second into one segment.

## Related Docs

- [Auditing](../concepts/auditing.md)
- [PostgreSQL Audit Storage](auditing-postgresql.md)
- [Entity Framework Core Audit Storage](auditing-entity-framework-core.md)
- [Marten Unit of Work, Idempotency, and Audit](commands-marten.md)
