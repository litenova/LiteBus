# Query and Purge Store Roles

- **ID**: `storage.role.query-purge`
- **Name**: Query and purge store roles
- **Maturity**: GA
- **Summary**: Filtered message queries and destructive purge for operator tooling.

## What It Does

`IInboxMessageQuery` / `IOutboxMessageQuery` return paged envelopes matching filters (status, contract, time range, tenant, correlation). `IInboxPurgeStore` / `IOutboxPurgeStore` delete rows matching purge predicates for support and test cleanup. These roles sit on the operations composite store alongside diagnostics and dead-letter APIs. Managers and ASP.NET management endpoints expose query and purge to authenticated operators.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `IInboxMessageQuery` / `IOutboxMessageQuery` | Filtered paged reads |
| `IInboxPurgeStore` / `IOutboxPurgeStore` | Predicate-based destructive delete |
| `InboxMessagePage` / `OutboxMessagePage` | Continuation-token paging results |
| Query and purge filter types | Status, contract, time range, tenant, correlation predicates |

### Invocation

| Method | Typical caller |
| --- | --- |
| `QueryAsync` | `IInboxManager` / `IOutboxManager`, management HTTP endpoints |
| `PurgeAsync` | Operator tooling, test cleanup, management HTTP endpoints |

### Registration

- Operations composite on singleton store; managers resolve query and purge interfaces from DI.

### Configuration

- Authentication and authorization at application edge for production purge (not configured on store).

### Extension Points

- Custom stores must honor paging contracts and return purge affected-row counts.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | Query and purge interfaces |
| `LiteBus.Outbox.Abstractions` | Query and purge interfaces |
| `LiteBus.Extensions.AspNetCore` | HTTP management surfaces |

## Requires

- Operations composite store on singleton adapter
- Authentication/authorization at application edge for production purge

## Invariants

- Query returns `InboxMessagePage` / `OutboxMessagePage` with continuation tokens per store policy.
- Purge is destructive and not limited to terminal rows unless the filter says so.
- Purge and retention serve different purposes: retention is scheduled terminal cleanup; purge is operator-driven filtered delete.

## Non-Goals

- No full-text search inside JSON payloads.
- No cross-database federated query.
- Does not export messages before purge.

## Observability

| Signal | Source | Notes |
| --- | --- | --- |
| Purge affected row count | `PurgeAsync` return value | Applications should audit operator purge calls |
| Queue depth change | `litebus.inbox.queue.depth` / `litebus.outbox.queue.depth` | Counts drop for purged statuses on next scrape |
| Management HTTP | Application logs | No built-in LiteBus audit log for query/purge |

No dedicated query or purge OpenTelemetry instruments.

## Deep Docs

- [Operations management](../durable-core/operations-management.md)
- [ASP.NET management endpoints](../hosting/aspnet-management-endpoints.md)

## Test Coverage

### Covered

#### `InboxStoreContractTests.QueryAsync_ShouldFilterAndPageByCreatedAt`

- **Use case**: Filter and page inbox query by created_at
- **Test kind**: Contract
- **Description**: Multiple inbox rows with varied timestamps
- **Behavior**: `QueryAsync` with time filter and page size
- **Expected outcome**: Page contains matching rows; continuation token when more exist
- **Remarks**: `LiteBus.Storage.Testing`

#### `OutboxStoreContractTests.QueryAsync_ShouldFilterAndPageByCreatedAt`

- **Use case**: Filter and page outbox query by created_at
- **Test kind**: Contract
- **Description**: Multiple outbox rows with varied timestamps
- **Behavior**: `QueryAsync` with time filter and page size
- **Expected outcome**: Page contains matching rows; continuation when more exist
- **Remarks**: Inherited by all outbox backends

#### `InboxStoreContractTests.PurgeAsync_ShouldDeleteMatchingRows`

- **Use case**: Purge inbox rows matching predicate
- **Test kind**: Contract
- **Description**: Rows matching and not matching purge filter
- **Behavior**: `PurgeAsync` with status or time predicate
- **Expected outcome**: Only matching rows deleted; count returned
- **Remarks**: Destructive operator path

#### `OutboxStoreContractTests.PurgeAsync_ShouldDeleteMatchingRows`

- **Use case**: Purge outbox rows matching predicate
- **Test kind**: Contract
- **Description**: Rows matching and not matching purge filter
- **Behavior**: `PurgeAsync`
- **Expected outcome**: Only matching rows deleted; count returned
- **Remarks**: Destructive operator path

### Untested

- ASP.NET management HTTP query and purge endpoints (hosting axis).
- Full-text search inside JSON payloads.

### Out-of-Scope

- Cross-database federated query across store instances.
- Export or backup before purge.
- Scheduled terminal cleanup (retention role).
