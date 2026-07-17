# Saga (Extension Tier)

**Maturity: Extension**

LiteBus saga support provides **correlated state persistence around inbox command dispatch**. It is a process-manager pattern: handlers mutate durable state keyed by `CorrelationId` while the inbox processor delivers commands at-least-once.

Saga is **not** a full workflow engine. There is no built-in scheduler, timeout framework, compensation DSL, or outbox-driven choreography.

## What It Is

- A processor hook (`SagaProcessorHook`) that loads saga state before dispatch and saves or completes it after successful dispatch.
- Handler access through injected `ISagaContext` (`GetState`, `SetState`, `Complete`).
- State keyed by `SagaCorrelation` (`CorrelationId`, `SagaDefinitionId`, optional `TenantId`).
- Explicit in-memory store for development or PostgreSQL persistence for production.
- Per-dispatch scope on the singleton `SagaExecutionContext`, safe for the processor's before/dispatch/after flow; optimistic concurrency when `DispatcherConcurrency` is greater than one.

## What It Is Not

| Not included | Use instead |
| --- | --- |
| `ISagaHandler<TCommand, TState>` (removed in v6.0) | `ICommandHandler<TCommand>` + `ISagaContext` |
| Outbox or event-driven saga | Inbox commands only |
| EF Core saga storage | PostgreSQL or custom `ISagaStore` |
| Saga scheduler / timeouts | Application timers or external orchestration |
| Built-in compensation framework | Handler logic and idempotent side effects |
| Transactional saga + inbox in one connection (v6.0) | Application-level two-phase workflow or future transactional store adapter |

## Packages to Install

| Package | Role |
| --- | --- |
| `LiteBus.Saga` | Core saga store, processor hook, state registry |
| `LiteBus.Saga.InboxIntegration` | `EnableSaga()` on `InboxModuleBuilder` |
| `LiteBus.Saga.Storage.PostgreSql` | PostgreSQL `ISagaStore` selection for production |

Reference inbox, command module, and storage packages as usual. See [Dependency Graph](../architecture/dependency-graph.md) for role rules.

## Registration

Enable saga on the inbox module builder. Map saga definition identifiers and contracts:

```csharp
builder.AddMessaging(_ => { });
builder.AddCommands(commands => commands.RegisterFromAssembly(typeof(AdvanceOrderSagaCommand).Assembly));
builder.AddInbox(inbox =>
{
    inbox.Contracts.Register<AdvanceOrderSagaCommand>("orders.saga.advance", 1);
    inbox.Contracts.Register<CompleteOrderSagaCommand>("orders.saga.complete", 1);
    inbox.UseInMemoryStorage();
    inbox.UseInProcessDispatch();
    inbox.EnableInboxProcessor();

    inbox.EnableSaga(saga =>
    {
        saga.DefineState<OrderSagaState>("order-workflow");
        saga.MapContract("orders.saga.advance", "order-workflow");
        saga.MapContract("orders.saga.complete", "order-workflow");
        saga.UseInMemoryStorage();

        // Production: replace UseInMemoryStorage with the PostgreSQL selection.
        // saga.UsePostgreSqlStorage(pg => pg.UseConnectionString(connectionString));
    });
});
```

Single-contract workflows may use `MapState<TState>(contractName)`, which registers the definition identifier and state type with the same name.

Register command handlers normally through `AddCommands`. Saga handlers are standard `ICommandHandler<TCommand>` implementations.

## Handler Pattern

Inject `ISagaContext` and mutate state during dispatch:

```csharp
public sealed class AdvanceOrderSagaCommandHandler : ICommandHandler<AdvanceOrderSagaCommand>
{
    private readonly ISagaContext _sagaContext;

    public AdvanceOrderSagaCommandHandler(ISagaContext sagaContext) => _sagaContext = sagaContext;

    public Task HandleAsync(AdvanceOrderSagaCommand command, CancellationToken cancellationToken = default)
    {
        if (!_sagaContext.IsActive)
        {
            return Task.CompletedTask;
        }

        var state = _sagaContext.GetState<OrderSagaState>();
        state.Step++;
        _sagaContext.SetState(state);
        return Task.CompletedTask;
    }
}
```

Call `Complete()` when the workflow reaches a terminal business state. The hook marks the saga instance completed after dispatch succeeds. Do not call `Complete()` and `SetState()` in the same dispatch; persist final state on one message and complete on a later correlated message.

## Correlation and Tenancy

Saga load and save require a correlation on the inbox envelope trace metadata:

```csharp
await inbox.AcceptAsync(
    InboxAcceptItem<AdvanceOrderSagaCommand>.From(
        new AdvanceOrderSagaCommand(orderId),
        InboxAcceptMetadata.Immediate with
        {
            Trace = new MessageTrace.Correlated(correlationId),
            Tenant = new TenantScope.Isolated(tenantId)
        }));
```

Messages without correlation dispatch normally but skip saga scope (`ISagaContext.IsActive` is false). `TenantId` on the envelope participates in the saga primary key when present. The PostgreSQL store writes a missing or whitespace tenant identifier as an empty string. An omitted tenant predicate on `SagaQueryFilter` or `SagaPurgeFilter` includes every tenant.

## Failure Semantics and Concurrency

| Scenario | Behavior |
| --- | --- |
| Handler throws | Dispatch fails; saga state is **not** saved for that attempt |
| Handler succeeds, `SetState` called | State persisted with optimistic concurrency (`SagaConcurrencyException` on conflict) |
| Dirty-state `SagaConcurrencyException` after dispatch | Hook propagates the conflict without reloading or replacing the concurrent state; inbox failure policy handles the message |
| Completion-only `SagaConcurrencyException` after dispatch | Hook reloads and retries against the current version up to three attempts; an already completed saga is treated as success |
| At-least-once redelivery | Handlers must be **idempotent**; always read current state with `GetState` before mutating |
| Completed saga | Further correlated messages dispatch without active saga scope |

`SagaExecutionContext` registers as a singleton. Each active scope is keyed by the durable message ID, and an instance `AsyncLocal` attaches that scope to the current dispatch flow. `SagaProcessorHook.PrepareDispatchScope` runs synchronously after `BeforeDispatchAsync` so the dispatcher inherits the attached scope. `SagaInboxCommandScopePreHandler` attaches the same message-keyed scope before in-process command handlers. Two messages for the same saga correlation keep independent in-memory snapshots until optimistic persistence resolves their version conflict. Failed dispatches call `AbandonDispatchScope` and remove their entries from the singleton context.

## Durability Model

Saga persistence is **separate** from inbox terminal state in v6.0:

1. Handler runs during inbox dispatch.
2. On success, `SagaProcessorHook.AfterDispatchAsync` saves saga state or marks completion.
3. Inbox terminal persistence (completed/failed) runs after hooks succeed.

Inbox and saga rows are **not** committed in one database transaction unless the application coordinates that explicitly. A process crash after saga save but before inbox completion can leave saga state advanced while the inbox message retries. Design handlers for at-least-once delivery: reload state, guard with business keys, and make side effects idempotent.

**Transactional inbox + saga commit (deferred):** A single ambient transaction spanning inbox terminal update and saga save would require a store-level seam (`ITransactionalInbox` + saga store participation). That interface is not shipped in v6.0 because saga and inbox stores use separate connections by default and EF/PostgreSQL transactional writers target message rows only. Until a dedicated adapter ships, use application-level coordination or accept at-least-once semantics with idempotent handlers. See [Roadmap](../roadmap/README.md).

## Store API

| Type | Role |
| --- | --- |
| `SagaSaveItem<TState>.From(...)` | Optimistic save with `ExpectedVersion` |
| `SagaCompleteItem.From(...)` | Completion with version check (no phantom rows) |
| `SagaQueryFilter` / `SagaPurgeFilter` | Operational query and retention on `ISagaStore` |

PostgreSQL schema version **1** includes the tenant-scoped primary key (`correlation_id`, `saga_type`, `tenant_id`), required indexes, and shared schema version metadata through `PostgreSqlSchemaManager` (same bootstrap rail as inbox and outbox). `EnsureSchemaCreationOnStartup` defaults to `true`.

## Options Reference

| Type | Package | Notes |
| --- | --- | --- |
| `SagaModuleBuilder.DefineState<TState>(id)` | `LiteBus.Saga` | Registers saga definition identifier and CLR state type |
| `SagaModuleBuilder.MapContract(name, id)` | `LiteBus.Saga` | Maps message contract to saga definition |
| `InboxModuleBuilder.EnableSaga(...)` | `LiteBus.Saga.InboxIntegration` | Registers the nested saga composition |
| `SagaModuleBuilder.UseInMemoryStorage()` | `LiteBus.Saga` | Explicit test and local storage selection |
| `SagaModuleBuilder.UsePostgreSqlStorage(...)` | `LiteBus.Saga.Storage.PostgreSql` | Explicit durable storage selection |
| `PostgreSqlSagaStoreOptions` | `LiteBus.Saga.Storage.PostgreSql` | `SchemaName`, `TableName`, schema startup flags |

Inbox processor options (`BatchSize`, `LeaseDuration`, and similar) apply unchanged. See [Inbox](inbox.md).

## Limits

| Area | v6 scope |
| --- | --- |
| Trigger | Inbox command dispatch only |
| Storage | Exactly one explicit selection: InMemory, PostgreSQL, or a custom `ISagaStorageModule` |
| Handler API | `ISagaContext` injection only |
| Orchestration | Application-owned multi-step logic in handlers |
| Outbox / events | Not integrated with saga hook |
| Transactional inbox + saga persist | Separate connections in v6.0; at-least-once with idempotent handlers (see Durability model) |

## Operations

Monitor inbox processor metrics and saga row growth when using PostgreSQL storage. Use `ISagaStore.QueryAsync` and `PurgeAsync` for retention jobs.

For dirty-state optimistic concurrency failures, retry the inbox message or reconcile state manually. Completion-only conflicts retry inside the hook up to three attempts.

## Tests

| Test | Proves |
| --- | --- |
| [`SagaProcessorHookTests`](../../tests/LiteBus.Storage.UnitTests/Saga/SagaProcessorHookTests.cs) | InMemory load/save around dispatch |
| [`InMemorySagaStoreTests`](../../tests/LiteBus.Storage.UnitTests/Saga/InMemorySagaStoreTests.cs) | Version conflicts, atomic completion, typed keys, timestamps, query, and purge behavior |
| [`PostgreSqlSagaInboxEndToEndTests`](../../tests/LiteBus.Storage.IntegrationTests/PostgreSql/PostgreSqlSagaInboxEndToEndTests.cs) | PostgreSQL persistence and tenant-scoped correlation after `EnableSaga` |
| [`PostgreSqlSagaStoreOperationsTests`](../../tests/LiteBus.Storage.IntegrationTests/PostgreSql/PostgreSqlSagaStoreOperationsTests.cs) | PostgreSQL tenant isolation, query, purge, and concurrent completion behavior |
| [`PostgreSqlSagaOrchestrationDepthTests`](../../tests/LiteBus.Storage.IntegrationTests/PostgreSql/PostgreSqlSagaOrchestrationDepthTests.cs) | Multi-step workflow, compensation, concurrency |

## See Also

- [Cookbook: Inbox saga with PostgreSQL](../getting-started/cookbook.md)
- [Architecture](../architecture/README.md): `IProcessorEnvelopeHook` orchestration model
- [Analyzers](../reference/analyzers.md): compile-time durable messaging rules
