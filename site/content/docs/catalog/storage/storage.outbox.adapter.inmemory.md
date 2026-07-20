# Outbox InMemory Adapter

- **ID**: `storage.outbox.adapter.inmemory`
- **Name**: Outbox InMemory adapter
- **Maturity**: GA
- **Summary**: Thread-safe in-process outbox store for tests and samples without a database.

## What It Does

`InMemoryOutboxStore` implements all outbox store roles under a process-wide lock. Registration:

```csharp
outbox.UseInMemoryStorage();
```

Pairs with in-process dispatch for full mediator loop tests.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `InMemoryOutboxStore` | All outbox store roles in-process |
| `InMemoryOutboxStorageModuleBuilder` | Test tuning options |
| `IOutboxStore` through `IOutboxOperationsStore` | Same abstractions as relational adapters |

### Invocation

| Method | Typical caller |
| --- | --- |
| `IOutboxStore.EnqueueAsync` | Unit tests, samples |
| `IOutboxLeaseStore.LeasePendingAsync` | Processor tests with in-process dispatch |
| `GetAll` (test helper) | Filter diagnostics in unit tests |

### Registration

```csharp
outbox.UseInMemoryStorage();
```

- Registers all outbox store roles on one singleton instance.

### Configuration

| Option surface | Purpose |
| --- | --- |
| `InMemoryOutboxStoreOptions` | Lease behavior and test defaults |
| Module builder | No external connectivity options |

### Extension Points

- None for durability; use PostgreSQL or EF adapter for production.

## Packages

| Package | Dependency role |
| --- | --- |
| `LiteBus.Outbox.Storage.InMemory` | Feature bridge |

## Requires

- Outbox module with dispatch adapter

## Invariants

- Behavioral parity with contract tests shared across backends.
- Data lost on process exit.

## Non-Goals

- Durability, NOTIFY, transactional DB enlistment

## Observability

| Meter | Instrument | Type | When observed | Tags | Storage coupling |
| --- | --- | --- | --- | --- | --- |
| `LiteBus.Outbox` | `litebus.outbox.queue.depth` | Observable gauge | OpenTelemetry scrape | `litebus.outbox.status` | In-memory diagnostics counts |
| `LiteBus.Outbox` | `litebus.outbox.processor.leases_acquired` | Counter | Processor pass in test host |: | In-memory lease store |
| `LiteBus.Outbox` | `litebus.outbox.processor.state` | Observable gauge | Processor loop | State enum | Test hosts with processor |

Register through `AddLiteBusOutboxMetrics()`. Constants on `LiteBusOutboxTelemetry`.

## Deep Docs

- [Outbox](../../reliable-messaging/outbox.md)
- [Feature Index](../../reference/feature-index-v6.md)

## Test Coverage

### Covered

#### `OutboxStoreContractTests` (in `LiteBus.Outbox.Storage.InMemory.UnitTests`)

- **Use case**: Full outbox store contract in memory
- **Test kind**: Contract (unit)
- **Description**: Complete shared outbox contract suite against `InMemoryOutboxStore`
- **Behavior**: Enqueue, lease, persist, query, purge, diagnostics
- **Expected outcome**: Parity with `LiteBus.Storage.Testing` expectations
- **Remarks**: `LiteBus.Outbox.Storage.InMemory.UnitTests`

#### `OutboxRetentionStoreContractTests` (in `LiteBus.Outbox.Storage.InMemory.UnitTests`)

- **Use case**: Full outbox retention contract in memory
- **Test kind**: Contract (unit)
- **Description**: Retention delete eligibility tests
- **Behavior**: `DeletePublishedOlderThanAsync` variants
- **Expected outcome**: Same outcomes as shared retention contract
- **Remarks**: `LiteBus.Outbox.Storage.InMemory.UnitTests`

#### `InMemoryOutboxStorageModuleTests.UseInMemoryStorage_ShouldRegisterAllStoreRoles`

- **Use case**: Register all outbox store roles
- **Test kind**: Unit
- **Description**: Build module with in-memory storage
- **Behavior**: Resolve store interfaces
- **Expected outcome**: All roles on singleton
- **Remarks**: `LiteBus.Outbox.Storage.InMemory.UnitTests`

#### `InMemoryOutboxStoreLeaseAvailabilityTests.LeasePendingAsync_WhenPublishingWithNullLeaseExpiry_ShouldNotLeaseImmediately`

- **Use case**: Null lease expiry blocks immediate reclaim
- **Test kind**: Unit
- **Description**: Row in publishing state with null lease expiry
- **Behavior**: Immediate lease attempt
- **Expected outcome**: Row not leased until stale
- **Remarks**: `LiteBus.Outbox.Storage.InMemory.UnitTests`

#### `InMemoryOutboxStoreLeaseAvailabilityTests.LeasePendingAsync_WhenPublishingWithNullLeaseExpiryBecomesStale_ShouldReclaim`

- **Use case**: Reclaim after null lease becomes stale
- **Test kind**: Unit
- **Description**: Publishing row with null expiry; advance time
- **Behavior**: Lease after stale threshold
- **Expected outcome**: Row reclaimed
- **Remarks**: `LiteBus.Outbox.Storage.InMemory.UnitTests`

#### `InMemoryOutboxIdempotencyIndexTests.DeletePublishedOlderThanAsync_ShouldRemoveIdempotencyIndexEntry`

- **Use case**: Retention removes idempotency index entry
- **Test kind**: Unit
- **Description**: Published row eligible for retention delete
- **Behavior**: `DeletePublishedOlderThanAsync`
- **Expected outcome**: Row and idempotency index entry removed
- **Remarks**: `LiteBus.Outbox.Storage.InMemory.UnitTests`

#### `InMemoryOutboxIdempotencyIndexTests.Clear_ShouldRemoveIdempotencyIndexEntry`

- **Use case**: Clear store removes idempotency index entry
- **Test kind**: Unit
- **Description**: Store with idempotency-keyed row
- **Behavior**: Test `Clear` helper
- **Expected outcome**: Index entry removed with store clear
- **Remarks**: `LiteBus.Outbox.Storage.InMemory.UnitTests`

#### `InMemoryOutboxGetAllFilterTests.GetAll_WithStatusAndContractName_ShouldFilterResults`

- **Use case**: Filter InMemory outbox via GetAll test helper
- **Test kind**: Unit
- **Description**: Test-only filter by status and contract
- **Behavior**: `GetAll` predicates
- **Expected outcome**: Filtered subset returned
- **Remarks**: Test-only surface; `LiteBus.Outbox.Storage.InMemory.UnitTests`

### Untested

- InMemory outbox capacity limits (inbox InMemory has capacity tests; outbox does not).

### Out-of-Scope

- Durability, NOTIFY, transactional database enlistment.
- Broker publish.
