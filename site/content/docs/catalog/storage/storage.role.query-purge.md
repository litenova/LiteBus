# Query and Purge Store Roles

- **ID**: `storage.role.query-purge`
- **Name**: Query and purge store roles
- **Maturity**: GA
- **Summary**: Provides filtered message queries and destructive purge for operator tooling.

## What It Does

`IInboxMessageQuery` / `IOutboxMessageQuery` return paged envelopes matching filters (status, contract, time range, tenant, correlation). `IInboxPurgeStore` / `IOutboxPurgeStore` delete rows matching purge predicates for support and test cleanup. These roles sit on the operations composite store alongside diagnostics and dead-letter APIs. Managers and ASP.NET management endpoints expose query and purge to authenticated operators.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `IInboxMessageQuery` / `IOutboxMessageQuery` | Filtered, paged reads |
| `IInboxPurgeStore` / `IOutboxPurgeStore` | Predicate-based destructive delete |
| `InboxMessagePage` / `OutboxMessagePage` | Continuation-token paging results |
| Query and purge filter types | Status, contract, time range, tenant, correlation predicates |

### Invocation

| Method | Typical caller |
| --- | --- |
| `QueryAsync` | `IInboxManager` / `IOutboxManager`, management HTTP endpoints |
| `PurgeAsync` | Operator tooling, test cleanup, management HTTP endpoints |

### Registration

- The operations composite sits on the singleton store. Managers resolve the query and purge interfaces from DI.

### Configuration

- Authentication and authorization stay at the application edge for production purge. The store configures nothing.

### Extension Points

- Custom stores must honor the paging contracts and return purge affected-row counts.

## Packages

| Package | Role |
| --- | --- |
| `LiteBus.Inbox.Abstractions` | Query and purge interfaces |
| `LiteBus.Outbox.Abstractions` | Query and purge interfaces |
| `LiteBus.Extensions.AspNetCore` | HTTP management surfaces |

## Requires

- An operations composite store on the singleton adapter
- Authentication and authorization at the application edge for production purge

## Invariants

- Query returns `InboxMessagePage` / `OutboxMessagePage` with continuation tokens per store policy.
- Purge is destructive and not limited to terminal rows unless the filter says so.
- Purge and retention serve different purposes. Retention is scheduled terminal cleanup, while purge is operator-driven filtered delete.
- A null filter tenant means no tenant restriction. An empty string matches only unscoped rows. Stored and requested tenants are compared through normalized values on every adapter.

## Non-Goals

- It does not search full text inside JSON payloads.
- It does not query across databases.
- It does not export messages before purge.

## Observability

| Signal | Source | Notes |
| --- | --- | --- |
| Purge affected row count | `PurgeAsync` return value | Applications should audit operator purge calls |
| Queue depth change | `litebus.inbox.queue.depth` / `litebus.outbox.queue.depth` | Counts for purged statuses drop on the next scrape |
| Management HTTP | Application logs | No built-in LiteBus audit log for query and purge |

There are no dedicated query or purge OpenTelemetry instruments.

## Further Reading

- [Operations management](../durable-core/operations-management.md)
- [ASP.NET management endpoints](../hosting/aspnet-management-endpoints.md)

## Test Coverage

### Covered

#### `InboxStoreContractTests.QueryAsync_ShouldFilterAndPageByCreatedAt`

- **Use case**: Filter and page inbox query by created_at
- **Test kind**: Contract
- **Description**: Seeds multiple inbox rows with varied timestamps
- **Behavior**: Runs `QueryAsync` with a time filter and page size
- **Expected outcome**: The page contains the matching rows, with a continuation token when more exist
- **Remarks**: `LiteBus.Storage.Testing`

#### `OutboxStoreContractTests.QueryAsync_ShouldFilterAndPageByCreatedAt`

- **Use case**: Filter and page outbox query by created_at
- **Test kind**: Contract
- **Description**: Seeds multiple outbox rows with varied timestamps
- **Behavior**: Runs `QueryAsync` with a time filter and page size
- **Expected outcome**: The page contains the matching rows, with continuation when more exist
- **Remarks**: Inherited by all outbox backends

#### `InboxStoreContractTests.PurgeAsync_ShouldDeleteMatchingRows`

- **Use case**: Purge inbox rows matching predicate
- **Test kind**: Contract
- **Description**: Seeds rows that match and do not match the purge filter
- **Behavior**: Runs `PurgeAsync` with a status or time predicate
- **Expected outcome**: Only matching rows are deleted, and the count is returned
- **Remarks**: Destructive operator path

#### `OutboxStoreContractTests.PurgeAsync_ShouldDeleteMatchingRows`

- **Use case**: Purge outbox rows matching predicate
- **Test kind**: Contract
- **Description**: Seeds rows that match and do not match the purge filter
- **Behavior**: Runs `PurgeAsync`
- **Expected outcome**: Only matching rows are deleted, and the count is returned
- **Remarks**: Destructive operator path

### Untested

- ASP.NET management HTTP query and purge endpoints in the hosting axis.
- Full-text search inside JSON payloads.

### Out-of-Scope

- Cross-database federated query across store instances.
- Export or backup before purge.
- Scheduled terminal cleanup, which belongs to the retention role.
