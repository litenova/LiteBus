# Outbox InMemory Adapter

- **ID**: `storage.outbox.adapter.inmemory`
- **Name**: Outbox InMemory adapter
- **Maturity**: GA
- **Summary**: Thread-safe, in-process outbox store for tests and samples that run without a database.

## What It Does

`InMemoryOutboxStore` implements all outbox store roles under a process-wide lock. Register it with:

```csharp
outbox.UseInMemoryStorage();
```

Combine it with in-process dispatch to test the full mediator loop.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `InMemoryOutboxStore` | Implements all outbox store roles in process |
| `InMemoryOutboxStorageModuleBuilder` | Options for tuning tests |
| `IOutboxStore` through `IOutboxOperationsStore` | Same abstractions as relational adapters |

### Invocation

| Method | Typical caller |
| --- | --- |
| `IOutboxStore.AddAsync` | Unit tests, samples |
| `IOutboxLeaseStore.LeasePendingAsync` | Processor tests with in-process dispatch |
| `ILeaseRenewable.ReleaseLeaseAsync` | Processor tests for cancelled dispatch |
| `GetAll` (test helper) | Unit tests that filter diagnostics |

### Registration

```csharp
outbox.UseInMemoryStorage(o => o.UseTimeProvider(timeProvider));
```

- Registers all outbox store roles on one singleton instance.
- A second `UseInMemoryStorage` call on the same builder throws.

### Configuration

| Option surface | Purpose |
| --- | --- |
| `InMemoryOutboxStoreOptions.Capacity` | Throws on append when capacity is exceeded (unless idempotent). Zero means unbounded |
| `InMemoryOutboxStoreOptions.DefaultLeaseDuration` | Defaults to one minute and must be positive. The store validates it at construction |
| `UseOptions` | Replaces the store options |
| `UseTimeProvider` | Makes lease expiry deterministic in tests |
| Module builder | Offers no external connectivity options |

### Extension Points

- There are no durability extensions. Use the PostgreSQL or EF adapter for production.

## Packages

| Package | Dependency role |
| --- | --- |
| `LiteBus.Outbox.Storage.InMemory` | Feature bridge |

## Requires

- A registered dispatcher, when the processor is enabled

## Invariants

- The store keeps behavioral parity with the contract tests shared across backends.
- Data is lost on process exit.
- The store normalizes tenants. A null filter or lease tenant means no restriction, while an empty string matches only unscoped rows.
- Persist validates statuses up front and throws `InvalidOperationException` for non-terminal envelopes, matching relational adapters.
- Leased and queried envelopes carry the recorded `Handler` value, so per-handler delivery works without a database.

## Non-Goals

- Durability, NOTIFY, and transactional database enlistment

## Observability

| Meter | Instrument | Type | When observed | Tags | Storage coupling |
| --- | --- | --- | --- | --- | --- |
| `LiteBus.Outbox` | `litebus.outbox.queue.depth` | Observable gauge | OpenTelemetry scrape | `litebus.outbox.status` | In-memory diagnostics counts |
| `LiteBus.Outbox` | `litebus.outbox.processor.leases_acquired` | Counter | Processor pass in test host |: | In-memory lease store |
| `LiteBus.Outbox` | `litebus.outbox.processor.state` | Observable gauge | Processor loop | State enum | Test hosts with processor |

Register them through `AddLiteBusOutboxMetrics()`. The instrument names are constants on `LiteBusOutboxTelemetry`.

## Further Reading

- [Outbox](../../reliable-messaging/outbox.md)
- [Feature Index](../../reference/feature-index.md)

## Test Coverage

### Covered

#### `OutboxStoreContractTests` (in `LiteBus.Storage.UnitTests`, shared base in `LiteBus.Storage.Testing`)

- **Use case**: Full outbox store contract in memory
- **Test kind**: Contract (unit)
- **Description**: Runs the complete shared outbox contract suite against `InMemoryOutboxStore`
- **Behavior**: Covers enqueue, lease, persist, query, purge, and diagnostics
- **Expected outcome**: Results match the `LiteBus.Storage.Testing` expectations
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `OutboxRetentionStoreContractTests` (in `LiteBus.Storage.UnitTests`, shared base in `LiteBus.Storage.Testing`)

- **Use case**: Full outbox retention contract in memory
- **Test kind**: Contract (unit)
- **Description**: Tests retention delete eligibility
- **Behavior**: Exercises `DeletePublishedOlderThanAsync` variants
- **Expected outcome**: Outcomes match the shared retention contract
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `InMemoryOutboxStorageModuleTests.UseInMemoryStorage_ShouldRegisterAllStoreRoles`

- **Use case**: Register all outbox store roles
- **Test kind**: Unit
- **Description**: Builds the module with in-memory storage
- **Behavior**: Resolves the store interfaces
- **Expected outcome**: All roles resolve to the singleton
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `InMemoryOutboxStoreLeaseAvailabilityTests.LeasePendingAsync_WhenPublishingWithNullLeaseExpiry_ShouldNotLeaseImmediately`

- **Use case**: Null lease expiry blocks immediate reclaim
- **Test kind**: Unit
- **Description**: Seeds a row in publishing state with null lease expiry
- **Behavior**: Attempts an immediate lease
- **Expected outcome**: The row is not leased until it is stale
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `InMemoryOutboxStoreLeaseAvailabilityTests.LeasePendingAsync_WhenPublishingWithNullLeaseExpiryBecomesStale_ShouldReclaim`

- **Use case**: Reclaim after null lease becomes stale
- **Test kind**: Unit
- **Description**: Seeds a publishing row with null expiry and advances time
- **Behavior**: Leases after the stale threshold
- **Expected outcome**: The row is reclaimed
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `InMemoryOutboxIdempotencyIndexTests.DeletePublishedOlderThanAsync_ShouldRemoveIdempotencyIndexEntry`

- **Use case**: Retention removes idempotency index entry
- **Test kind**: Unit
- **Description**: Seeds a published row eligible for retention delete
- **Behavior**: Runs `DeletePublishedOlderThanAsync`
- **Expected outcome**: The row and its idempotency index entry are removed
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `InMemoryOutboxIdempotencyIndexTests.Clear_ShouldRemoveIdempotencyIndexEntry`

- **Use case**: Clear store removes idempotency index entry
- **Test kind**: Unit
- **Description**: Seeds a store with an idempotency-keyed row
- **Behavior**: Runs the test `Clear` helper
- **Expected outcome**: The index entry is removed with the store clear
- **Remarks**: `LiteBus.Storage.UnitTests`

#### `InMemoryOutboxGetAllFilterTests.GetAll_WithStatusAndContractName_ShouldFilterResults`

- **Use case**: Filter InMemory outbox via GetAll test helper
- **Test kind**: Unit
- **Description**: Filters by status and contract with the test-only helper
- **Behavior**: Applies `GetAll` predicates
- **Expected outcome**: The filtered subset is returned
- **Remarks**: Test-only surface; `LiteBus.Storage.UnitTests`

### Untested

- InMemory outbox capacity limits are untested. The inbox InMemory store has capacity tests. The outbox does not.

### Out-of-Scope

- Durability, NOTIFY, and transactional database enlistment.
- Publishing to a broker.
