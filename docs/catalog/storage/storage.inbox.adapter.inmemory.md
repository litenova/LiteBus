# Inbox InMemory Adapter

- **ID**: `storage.inbox.adapter.inmemory`
- **Name**: Inbox InMemory adapter
- **Maturity**: GA
- **Summary**: Thread-safe in-process inbox store for unit tests, samples, and local development without external database dependencies.

## What It Does

`InMemoryInboxStore` implements all inbox store roles in memory with locking semantics aligned to PostgreSQL contract tests. Registration:

```csharp
inbox.UseInMemoryStorage();
```

Optional builder configures tenant defaults and strictness for tests. No schema management, NOTIFY, or transactional bindings.

## Public Surface

### Consumer Contracts

| Type | Role |
| --- | --- |
| `InMemoryInboxStore` | All inbox store roles in-process |
| `InMemoryInboxStorageModuleBuilder` | Capacity, lease duration, `TimeProvider` |
| `IInboxStore` through `IInboxOperationsStore` | Same abstractions as relational adapters |

### Invocation

| Method | Typical caller |
| --- | --- |
| `IInboxStore.AddAsync` | Unit tests, samples, local dev |
| `IInboxLeaseStore.LeasePendingAsync` | Processor tests with in-process dispatch |
| `GetAll` (test helper) | Filter diagnostics in unit tests |

### Registration

```csharp
inbox.UseInMemoryStorage(o => o.UseTimeProvider(timeProvider));
```

- Registers all inbox store roles on one singleton instance.
- Second `UseInMemoryStorage` on same builder throws.

### Configuration

| Option surface | Purpose |
| --- | --- |
| `InMemoryInboxStoreOptions.MaxCapacity` | Throw on append when capacity exceeded (unless idempotent) |
| `InMemoryInboxStoreOptions.DefaultLeaseDuration` | Lease duration when zero passed to lease API |
| `UseTimeProvider` | Deterministic lease expiry in tests |

### Extension Points

- None for durability or cross-process visibility; swap to PostgreSQL or EF adapter for production.

## Packages

| Package | Layer |
| --- | --- |
| `LiteBus.Inbox.Storage.InMemory` | 4 |

No external NuGet dependencies beyond inbox abstractions.

## Requires

- `AddInboxModule` with dispatch for processor tests

## Invariants

- Contract behavior matches relational stores per `InboxStoreContractTests`.
- Process restart clears all data (non-durable by design).

## Non-Goals

- Production durability
- Cross-process visibility
- Transactional participation with real databases

## Observability

| Meter | Instrument | Type | When observed | Tags | Storage coupling |
| --- | --- | --- | --- | --- | --- |
| `LiteBus.Inbox` | `litebus.inbox.queue.depth` | Observable gauge | OpenTelemetry scrape | `litebus.inbox.status` | In-memory counts from diagnostics role |
| `LiteBus.Inbox` | `litebus.inbox.processor.leases_acquired` | Counter | Processor pass in test host |: | In-memory lease store |
| `LiteBus.Inbox` | `litebus.inbox.processor.state` | Observable gauge | Processor loop | State enum | Test hosts with processor enabled |

In-memory stores report synthetic schema info suitable for tests. Register through `AddLiteBusInboxMetrics()`. Constants on `LiteBusInboxTelemetry`.

## Deep Docs

- [Reliable messaging](../../reliable-messaging/README.md)
- [v6 feature index: Storage](../../reference/feature-index-v6.md)

## Test Coverage

### Covered

#### `InboxStoreContractTests` (in `LiteBus.Inbox.Storage.InMemory.UnitTests`)

- **Use case**: Full inbox store contract in memory
- **Test kind**: Contract (unit)
- **Description**: Complete shared inbox contract suite against `InMemoryInboxStore`
- **Behavior**: All append, lease, persist, query, diagnostics tests
- **Expected outcome**: Parity with `LiteBus.Storage.Testing` expectations
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests`

#### `InboxRetentionStoreContractTests` (in `LiteBus.Inbox.Storage.InMemory.UnitTests`)

- **Use case**: Full inbox retention contract in memory
- **Test kind**: Contract (unit)
- **Description**: Retention delete eligibility tests
- **Behavior**: `DeleteCompletedOlderThanAsync` variants
- **Expected outcome**: Same outcomes as shared retention contract
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests`

#### `InMemoryInboxStorageModuleTests.UseInMemoryStorage_ShouldRegisterAllStoreRoles`

- **Use case**: Register all inbox store roles
- **Test kind**: Unit
- **Description**: Build module with in-memory storage
- **Behavior**: Resolve store interfaces
- **Expected outcome**: All roles registered on singleton
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests`

#### `InMemoryInboxStorageModuleTests.UseInMemoryStorage_WithCustomTimeProvider_ShouldRegisterTimeProvider`

- **Use case**: Register custom TimeProvider on module
- **Test kind**: Unit
- **Description**: Pass custom `TimeProvider` to builder
- **Behavior**: Resolve store and check time source
- **Expected outcome**: Custom provider used for lease timing
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests`

#### `InMemoryInboxStorageModuleTests.UseInMemoryStorage_WhenCalledTwiceOnBuilder_ShouldThrow`

- **Use case**: Throw when UseInMemoryStorage called twice
- **Test kind**: Unit
- **Description**: Duplicate storage registration on same builder
- **Behavior**: Second `UseInMemoryStorage` call
- **Expected outcome**: Configuration exception
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests`

#### `InMemoryInboxStoreOptionsTests.AddAsync_WhenCapacityReached_ShouldThrow`

- **Use case**: Throw when capacity reached on append
- **Test kind**: Unit
- **Description**: Store at max capacity
- **Behavior**: Append new non-idempotent message
- **Expected outcome**: Capacity exception thrown
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests`

#### `InMemoryInboxStoreOptionsTests.AddAsync_WhenCapacityReachedButSubmissionIsIdempotent_ShouldReturnExistingCommand`

- **Use case**: Return existing row when capacity reached but idempotent
- **Test kind**: Unit
- **Description**: Store at capacity; duplicate idempotency key
- **Behavior**: Append with existing idempotency key
- **Expected outcome**: Existing envelope returned without throw
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests`

#### `InMemoryInboxStoreOptionsTests.LeasePendingAsync_WhenLeaseDurationIsZero_ShouldUseDefaultLeaseDuration`

- **Use case**: Default lease duration when zero configured
- **Test kind**: Unit
- **Description**: Lease with zero duration parameter
- **Behavior**: `LeasePendingAsync`
- **Expected outcome**: Default from options applied
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests`

#### `InMemoryInboxStoreOptionsTests.LeasePendingAsync_WhenNowIsOmitted_ShouldUseTimeProviderForLeaseExpiry`

- **Use case**: TimeProvider drives lease expiry when now omitted
- **Test kind**: Unit
- **Description**: Custom `TimeProvider` on store
- **Behavior**: Lease without explicit `now`; advance time
- **Expected outcome**: Reclaim follows provider clock
- **Remarks**: `LiteBus.Inbox.Storage.InMemory.UnitTests`

#### `InMemoryInboxGetAllFilterTests.GetAll_WithStatusAndContractName_ShouldFilterResults`

- **Use case**: Filter InMemory inbox via GetAll test helper
- **Test kind**: Unit
- **Description**: Test-only `GetAll` with status and contract filters
- **Behavior**: Filter predicates
- **Expected outcome**: Matching subset returned
- **Remarks**: Test-only surface; `LiteBus.Inbox.Storage.InMemory.UnitTests`

### Untested

- Cross-process visibility between two hosts (by design in-process only).

### Out-of-Scope

- Production durability and NOTIFY.
- Transactional participation with real databases.
- Schema management.
