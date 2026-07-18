# Storage Axis Capability Catalog

The storage axis covers durable persistence for inbox and outbox flows. Technology-adapter packages own shared PostgreSQL and EF Core primitives, while feature bridges implement storage roles for each durable axis.

## Axis Role

- **Technology adapters**: shared storage infrastructure (`LiteBus.Storage.PostgreSql`, plus EF Core shared utilities outside this catalog).
- **Feature bridges**: axis adapters (`LiteBus.Inbox.Storage.*`, `LiteBus.Outbox.Storage.*`).
- **Primary responsibility**: accept and enqueue writes, processor leasing, state transitions, dead-letter replay, retention cleanup, diagnostics counts, and schema lifecycle for PostgreSQL adapters.

## Store Role Taxonomy

| Role | Inbox interface | Outbox interface | Main behavior |
| --- | --- | --- | --- |
| Append | `IInboxStore` | `IOutboxStore` | Insert pending rows with idempotency handling |
| Lease | `IInboxLeaseStore` | `IOutboxLeaseStore` | Claim due rows for one processor pass |
| State writer | `IInboxStateWriter` | `IOutboxStateWriter` | Persist completed, failed, or dead-letter outcomes |
| Dead letter | `IInboxDeadLetterStore` | `IOutboxDeadLetterStore` | Requeue dead-letter rows |
| Retention | `IInboxRetentionStore` | `IOutboxRetentionStore` | Delete terminal rows older than a cutoff |
| Diagnostics | `IInboxDiagnosticsStore` | `IOutboxDiagnosticsStore` | Status counts and schema info |
| Processing composite | `IInboxProcessingStore` | `IOutboxProcessingStore` | Lease plus state writer |
| Operations composite | `IInboxOperationsStore` | `IOutboxOperationsStore` | Dead letter, retention, diagnostics, query, purge |
| Transactional append | `ITransactionalInboxStore` | `ITransactionalOutboxStore` | Append inside caller transaction boundary |

## Package Table

| Package | Dependency role | Role in storage axis |
| --- | --- | --- |
| `LiteBus.Storage.PostgreSql` | Technology adapter | Shared SQL utilities, schema lock/version helpers, work signal, ambient transaction provider contract |
| `LiteBus.Inbox.Storage.PostgreSql` | Feature bridge | PostgreSQL inbox adapter, schema scripts, transactional inbox participant |
| `LiteBus.Outbox.Storage.PostgreSql` | Feature bridge | PostgreSQL outbox adapter, schema scripts, transactional outbox participant |

## Capability Index

| Capability | Focus |
| --- | --- |
| [storage.inbox.adapter.postgresql](storage.inbox.adapter.postgresql.md) | Full PostgreSQL inbox adapter surface |
| [storage.outbox.adapter.postgresql](storage.outbox.adapter.postgresql.md) | Full PostgreSQL outbox adapter surface |
| [storage.postgresql.shared-infra](storage.postgresql.shared-infra.md) | Shared PostgreSQL technology primitives |
| [storage.postgresql.schema-management](storage.postgresql.schema-management.md) | Ensure, validate, and drift checks |
| [storage.postgresql.work-signal](storage.postgresql.work-signal.md) | LISTEN/NOTIFY wake-up path |
| [storage.composite.processing-store](storage.composite.processing-store.md) | Composite lease/state role contracts |
| [storage.role.append](storage.role.append.md) | Append semantics and idempotency |
| [storage.role.lease](storage.role.lease.md) | Lease claim semantics and concurrency |
| [storage.role.state-writer](storage.role.state-writer.md) | Terminal state persistence |
| [storage.role.dead-letter](storage.role.dead-letter.md) | Manual replay path |
| [storage.role.retention](storage.role.retention.md) | Retention cutoff and deletes |
| [storage.transactional.writes](storage.transactional.writes.md) | Transaction-scoped inbox/outbox writes |

## Test Projects

- `LiteBus.Storage.UnitTests`
  - role contracts, PostgreSQL utility tests, transactional participant behavior, and module dependency validation.
- `LiteBus.Storage.IntegrationTests`
  - `PostgreSql/` suite for inbox/outbox store contracts, schema scripts, schema drift, work signal, end-to-end processor behavior, and transactional write integration.

## Related Main Documentation

These adapters are documented in main docs and are intentionally out of this PostgreSQL-only catalog scope:

- [Inbox EntityFrameworkCore Storage](../../integrations/inbox-ef-core-storage.md)
- [Outbox EntityFrameworkCore Storage](../../integrations/outbox-ef-core-storage.md)
- [Reliable Messaging](../../reliable-messaging/README.md)
- [Architecture](../../architecture/README.md#storage-axis)
