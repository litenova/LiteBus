# Saga (Extension Tier)

LiteBus saga support keeps correlated state across inbox command dispatches, so a multi-step workflow can track progress while the inbox delivers each command at least once. This page shows how to register sagas, write handlers, and handle failures.

**Maturity: Extension**

> **This page is for** developers who track a multi-step workflow across inbox commands.
>
> **After reading it** you can register saga state, write handlers with `ISagaContext`, and predict failure behavior. For the durable overview, start with [Reliable Messaging](README.md).

Saga is not a full workflow engine. There is no built-in scheduler, timeout framework, compensation language, or outbox-driven choreography.

## What It Is

- A processor hook (`SagaProcessorHook`) that loads saga state before dispatch and saves or completes it after successful dispatch.
- Handler access through injected `ISagaContext` (`GetState`, `SetState`, `Complete`).
- State keyed by `SagaCorrelation` (`CorrelationId`, `SagaDefinitionId`, optional `TenantId`).
- Explicit in-memory store for development or PostgreSQL persistence for production.
- Per-dispatch scope on the singleton `SagaExecutionContext`, with an in-process correlation gate and durable optimistic concurrency across hosts.

## What It Is Not

| Not included | Use instead |
| --- | --- |
| Dedicated saga handler type | `ICommandHandler<TCommand>` plus `ISagaContext` |
| Outbox or event-driven saga | Inbox commands only |
| EF Core saga storage | PostgreSQL or custom `ISagaStore` |
| Saga scheduler and timeouts | Application timers or external orchestration |
| Built-in compensation framework | Handler logic and idempotent side effects |
| Transactional saga plus inbox in one connection | Application-level two-phase workflow or future transactional store adapter |

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

Call `Complete()` when the workflow reaches a terminal business state. The hook marks the saga instance completed after dispatch succeeds. Do not combine `Complete()` with a state change in the same dispatch; persist final state on one message and complete on a later correlated message. Reading state with `GetState` and then calling `Complete()` is allowed. In-place mutations count as a state change: the hook compares the serialized state against the load snapshot, so mutated state is persisted even without `SetState()`.

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
| Handler throws | Dispatch fails; saga state is not saved for that attempt |
| Handler succeeds, `SetState` called | State persisted with optimistic concurrency (`SagaConcurrencyException` on conflict) |
| Dirty-state `SagaConcurrencyException` after dispatch | Hook propagates the conflict without reloading or replacing the concurrent state; inbox failure policy handles the message |
| Completion-only `SagaConcurrencyException` after dispatch | Hook reloads and retries against the current version up to three attempts; an already completed saga is treated as success |
| At-least-once redelivery | The store records only the last applied message ID. When an inbox terminal persist fails after a save, a newer message for the same correlation can advance the row before the failed message retries, and the retry then runs its handler again. Handlers and external side effects must remain idempotent across crashes; see [Idempotency](../concepts/idempotency.md) |
| Completed saga | Further correlated messages dispatch without active saga scope |

`SagaExecutionContext` registers as a singleton. Each active scope is keyed by the durable message ID, and an instance `AsyncLocal` attaches that scope to the current dispatch flow. `SagaProcessorHook.PrepareDispatchScope` runs synchronously after `BeforeDispatchAsync` so the dispatcher inherits the attached scope. `SagaInboxCommandScopePreHandler` attaches the same message-keyed scope before in-process command handlers. The hook serializes active dispatches for the same tenant, definition, and correlation inside one process. PostgreSQL optimistic concurrency remains the cross-process fence. Failed dispatches call `AbandonDispatchScope` and remove their entries from the singleton context.

Correlation serialization is not message ordering. Inbox acquisition is FIFO-like by `created_at`, but equal timestamps, retries, and multiple workers prevent a strict FIFO guarantee. Enqueue a causally dependent saga step only after its prerequisite is durable, or make the handler reject and retry an early step safely.

## Evolving State Shape

Saga state JSON carries no schema version. Renaming the state class breaks deserialization with a typed `MessageSerializationException`, but renaming a property is silent: unknown members are ignored and missing members take defaults, so an older row can deserialize into a state object that looks valid and has lost fields. Keep a `Version` property inside the state class that the handler checks and upgrades:

```csharp
public sealed class OrderSagaState
{
    public int Version { get; set; } = 2;
    public int Step { get; set; }
}
```

```csharp
var state = _sagaContext.GetState<OrderSagaState>();

if (state.Version < 2)
{
    // Rows written before Version existed deserialize with Version 0; map retired fields here.
    state.Version = 2;
    _sagaContext.SetState(state);
}
```

## Durability Model

Saga persistence is separate from inbox terminal state:

1. Handler runs during inbox dispatch.
2. On success, `SagaProcessorHook.AfterDispatchAsync` saves saga state or marks completion.
3. Inbox terminal persistence (completed or failed) runs after hooks succeed.

Inbox and saga rows are not committed in one database transaction unless the application coordinates that explicitly. A process crash after saga save but before inbox completion can leave saga state advanced while the inbox message retries. Design handlers for at-least-once delivery: reload state, guard with business keys, and make side effects idempotent.

A single ambient transaction spanning inbox terminal update and saga save requires a store-level seam (`ITransactionalInbox` plus saga store participation). The current API does not provide that seam because saga and inbox stores use separate connections by default and EF and PostgreSQL transactional writers target message rows only. Use application-level coordination or accept at-least-once semantics with idempotent handlers. See [Roadmap](../roadmap/README.md).

## Store API

| Type | Role |
| --- | --- |
| `SagaSaveItem<TState>.From(...)` | Optimistic save with `ExpectedVersion` |
| `SagaCompleteItem.From(...)` | Completion with version check (no phantom rows) |
| `SagaQueryFilter` and `SagaPurgeFilter` | Operational query and retention on `ISagaStore` |

PostgreSQL saga schema version **1** includes `last_applied_message_id` on the tenant-scoped row identified by (`correlation_id`, `saga_type`, `tenant_id`). Schema bootstrap uses `PostgreSqlSchemaManager`, the same validation path as inbox and outbox.

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

| Area | Current Behavior |
| --- | --- |
| Trigger | Inbox command dispatch only |
| Storage | Exactly one explicit selection: InMemory, PostgreSQL, or a custom `ISagaStorageModule` |
| Handler API | `ISagaContext` injection only |
| Orchestration | Application-owned multi-step logic in handlers |
| Outbox and events | Not integrated with saga hook |
| Transactional inbox plus saga persist | Separate connections; at-least-once with idempotent handlers (see Durability model) |

## Operations

Monitor inbox processor metrics and saga row growth when using PostgreSQL storage. Use `ISagaStore.QueryAsync` and `PurgeAsync` for retention jobs. Scope a purge with a predicate or bound it with `SagaPurgeFilter.Take`, which deletes the oldest matches first; a purge with no predicates and no limit is rejected.

For dirty-state optimistic concurrency failures, retry the inbox message or reconcile state manually. Completion-only conflicts retry inside the hook up to three attempts.

## Tests

| Test | Proves |
| --- | --- |
| [`SagaProcessorHookTests`](https://github.com/litenova/LiteBus/blob/main/tests/LiteBus.Storage.UnitTests/Saga/SagaProcessorHookTests.cs) | InMemory load and save around dispatch |
| [`InMemorySagaStoreTests`](https://github.com/litenova/LiteBus/blob/main/tests/LiteBus.Storage.UnitTests/Saga/InMemorySagaStoreTests.cs) | Version conflicts, atomic completion, typed keys, timestamps, query, and purge behavior |
| [`PostgreSqlSagaInboxEndToEndTests`](https://github.com/litenova/LiteBus/blob/main/tests/LiteBus.Storage.IntegrationTests/PostgreSql/PostgreSqlSagaInboxEndToEndTests.cs) | PostgreSQL persistence and tenant-scoped correlation after `EnableSaga` |
| [`PostgreSqlSagaStoreOperationsTests`](https://github.com/litenova/LiteBus/blob/main/tests/LiteBus.Storage.IntegrationTests/PostgreSql/PostgreSqlSagaStoreOperationsTests.cs) | PostgreSQL tenant isolation, query, purge, and concurrent completion behavior |
| [`PostgreSqlSagaOrchestrationDepthTests`](https://github.com/litenova/LiteBus/blob/main/tests/LiteBus.Storage.IntegrationTests/PostgreSql/PostgreSqlSagaOrchestrationDepthTests.cs) | Multi-step workflow, compensation, concurrency |

## Next

- [Cookbook: Inbox saga with PostgreSQL](../getting-started/cookbook.md)
- [Architecture](../architecture/README.md): `IProcessorEnvelopeHook` orchestration model
- [Analyzers](../reference/analyzers.md): compile-time durable messaging rules
