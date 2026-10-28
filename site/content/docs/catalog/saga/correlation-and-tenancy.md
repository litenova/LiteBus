# Correlation and Tenancy

- **ID**: `saga.correlation-and-tenancy`
- **Name**: Correlation and Tenancy
- **Maturity**: Extension
- **Summary**: A saga instance is keyed by the inbox correlation id, the saga definition id, and the optional tenant id.

## What It Does

`SagaCorrelation` identifies one saga instance in storage. `CorrelationId` comes from inbox trace metadata, and `SagaDefinitionId` comes from state registration. When inbox tenant metadata is present, `TenantId` comes from that metadata.

`SagaProcessorHook` builds the correlation from `IProcessorEnvelope` and the state registry mappings. If the correlation is missing, the hook does nothing. Dispatch then continues without an active `ISagaContext`.

## Public Surface

| Surface | Package | Role |
| --- | --- | --- |
| `SagaCorrelation` | `LiteBus.Saga.Abstractions` | Correlation value object (`CorrelationId`, `SagaDefinitionId`, `TenantId`) |
| `InboxAcceptMetadata.Trace` | `LiteBus.Inbox.Abstractions` | Entry point for correlated trace (`MessageTrace.Correlated(...)`) |
| `InboxAcceptMetadata.Tenant` | `LiteBus.Inbox.Abstractions` | Optional tenant scope that becomes part of the saga storage key |

## Packages

- `LiteBus.Saga.Abstractions`
- `LiteBus.Saga`

## Requires

- The inbox accept carries correlated trace metadata, so the dispatch participates in a saga.
- `saga.state-registration`, which resolves definition ids.

## Invariants

- When the correlation id is empty or whitespace, saga stays disabled for that dispatch.
- Related commands must carry the same tenant id to target one row.
- A completed saga row does not reactivate the handler context for mutation.

## Non-Goals

- The saga does not generate correlation ids automatically.
- The saga key does not enforce a causation chain.
- Unless tenant metadata is supplied, the saga provides no tenant isolation.

## Observability

Correlation construction has no dedicated observability.

Use the following instead:

- Log inbox accepts with their correlation and tenant ids at the application edge.
- Use tenant-scoped inbox metrics when tenant isolation is enabled on storage (durable-core axis).
- For support tooling, query the store with `QueryAsync` filtered by `CorrelationId` or `TenantId`.

## References

- [Architecture](../../architecture/README.md)
- [Roadmap](../../roadmap/README.md)
- [store](store.md)

## Test Coverage

### Covered Use Cases

#### `SagaProcessorHookTests.ProcessPendingAsync_PersistsSagaStateAfterSuccessfulDispatch`

- **Use case**: A correlated accept applies handler mutations to one saga instance keyed by the correlation id.
- **Test kind**: Unit
- **Description**: The test accepts with `MessageTrace.Correlated("order-42")`.
- **Behavior**: A single dispatch runs. The correlation matches the store load key.
- **Expected outcome**: The row is keyed by `order-42` and the `process-order` definition.
- **Remarks**: `tests/LiteBus.Storage.UnitTests/Saga/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_with_EnableSaga_should_persist_state_in_postgresql`

- **Use case**: The correlation from accept metadata flows through the hook to the PostgreSQL primary key.
- **Test kind**: Integration
- **Description**: The accept carries correlation id `order-9001`.
- **Behavior**: The test loads by `SagaCorrelation` after dispatch.
- **Expected outcome**: The matching PostgreSQL row exists.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaInboxEndToEndTests.ProcessPendingAsync_WithTenantScopedAccepts_ShouldPersistSeparateSagaRows`

- **Use case**: Inbox tenant metadata remains part of the saga key through dispatch and PostgreSQL persistence.
- **Test kind**: Integration
- **Description**: The test accepts two commands with one correlation identifier under `tenant-a` and `tenant-b`.
- **Behavior**: The hook creates tenant-scoped correlations. The store inserts two physical rows.
- **Expected outcome**: Each tenant loads state at step 1. An unscoped query returns both tenant identifiers.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `PostgreSqlSagaOrchestrationDepthTests.ProcessPendingAsync_two_workflow_steps_should_advance_single_saga_instance`

- **Use case**: Two accepts with the same correlation id advance one saga row.
- **Test kind**: Integration
- **Description**: The test reuses a random correlation across reserve and capture accepts.
- **Behavior**: The hook rebuilds the same correlation on each dispatch.
- **Expected outcome**: A single row remains at version 2.
- **Remarks**: `tests/LiteBus.Storage.IntegrationTests/PostgreSql/`.

#### `LiteBusV6CompositionSmokeTests.AddV6CompositionSmoke_ShouldPersistSagaStateAcrossCorrelatedCommands`

- **Use case**: Sample composition correlates multiple commands on one saga instance.
- **Test kind**: Composition
- **Description**: Two accepts carry a fixed correlation id.
- **Behavior**: The two processor passes share the correlation.
- **Expected outcome**: One instance reaches `Step == 2`.
- **Remarks**: `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/`.

### Out-of-Scope Use Cases

- Automatic correlation id generation.
- A `CausationId` in the saga primary key.
- Merging saga instances across tenants.
