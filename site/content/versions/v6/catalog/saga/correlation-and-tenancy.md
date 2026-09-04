# Correlation and Tenancy

- **ID**: `saga.correlation-and-tenancy`
- **Name**: Correlation and Tenancy
- **Maturity**: Extension
- **Summary**: Keys saga instances by inbox correlation id, saga definition id, and optional tenant id.

## What It Does

`SagaCorrelation` identifies one saga instance in storage. `CorrelationId` comes from inbox trace metadata, `SagaDefinitionId` comes from state registration, and `TenantId` comes from inbox tenant metadata when present.

`SagaProcessorHook` builds correlation from `IProcessorEnvelope` and state registry mappings. If correlation is missing, saga no-ops and dispatch continues without an active `ISagaContext`.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaCorrelation` | `LiteBus.Saga.Abstractions` | Correlation value object (`CorrelationId`, `SagaDefinitionId`, `TenantId`) |
| `InboxAcceptMetadata.Trace` | `LiteBus.Inbox.Abstractions` | Entry point for correlated trace (`MessageTrace.Correlated(...)`) |
| `InboxAcceptMetadata.Tenant` | `LiteBus.Inbox.Abstractions` | Optional tenant scope that joins saga storage key |

## Packages

- `LiteBus.Saga.Abstractions`
- `LiteBus.Saga`

## Requires

- Inbox accept with correlated trace metadata for saga participation
- `saga.state-registration` (definition id resolution)

## Invariants

- Empty or whitespace correlation id disables saga for that dispatch.
- Tenant id must match across related commands to target one row.
- Completed saga rows do not reactivate handler context for mutation.

## Non-Goals

- No automatic correlation id generation.
- No causation-chain enforcement in saga key shape.
- No tenant isolation unless tenant metadata is supplied.

## Observability

None specific to correlation construction.

**What to use instead**

- Inbox accept logging with correlation and tenant ids at application edge.
- Tenant-scoped inbox metrics when tenant isolation is enabled on storage (durable-core axis).
- Store `QueryAsync` filtered by `CorrelationId` or `TenantId` for support tooling.

## Deep Docs

- [Architecture](../../architecture/README.md)
- [Roadmap](../../roadmap/README.md)
- [store](store.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: Correlated accept converges handler mutations on one saga instance keyed by correlation id.
- **Test kind**: Unit
- **Description**: Accept with `MessageTrace.Correlated("order-42")`.
- **Behavior**: Single dispatch; correlation matches store load key.
- **Expected outcome**: Row keyed by `order-42` and `process-order` definition.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: Correlation from accept metadata flows through hook to PostgreSQL primary key.
- **Test kind**: Integration
- **Description**: Correlation id `order-9001` on accept.
- **Behavior**: Load by `SagaCorrelation` after dispatch.
- **Expected outcome**: Matching PostgreSQL row.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_WithTenantScopedAccepts_ShouldPersistSeparateSagaRows`

- **Use case**: Inbox tenant metadata remains part of the saga key through dispatch and PostgreSQL persistence.
- **Test kind**: Integration
- **Description**: Accepts two commands with one correlation identifier under `tenant-a` and `tenant-b`.
- **Behavior**: The hook creates tenant-scoped correlations, and the store inserts two physical rows.
- **Expected outcome**: Each tenant loads state with step 1, and an unscoped query returns both tenant identifiers.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: Two accepts with same correlation id advance one saga row.
- **Test kind**: Integration
- **Description**: Random correlation reused across reserve and capture accepts.
- **Behavior**: Hook rebuilds same correlation on each dispatch.
- **Expected outcome**: Single row; version 2.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample composition correlates multiple commands on one saga instance.
- **Test kind**: Composition
- **Description**: Fixed correlation id on two accepts.
- **Behavior**: Shared correlation across two processor passes.
- **Expected outcome**: `Step == 2` on one instance.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Out-of-Scope Use Cases

- Automatic correlation id generation.
- `CausationId` in saga primary key.
- Cross-tenant saga instance merging.
