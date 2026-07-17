# Saga Capability Catalog

LiteBus saga support is an Extension tier feature. It adds correlated state persistence around inbox command dispatch through `IProcessorEnvelopeHook` and `ISagaContext`.

Saga registration is composed from the inbox axis. There is no public top-level `AddSagaModule(...)` API in v6. Register saga through `registry.AddInboxModule(inbox => inbox.EnableSaga(...))`.

## Lifecycle

```text
Accept correlated inbox command
  -> lease inbox envelope
  -> SagaProcessorHook.BeforeDispatchAsync (load state)
  -> SagaProcessorHook.PrepareDispatchScope (attach message scope)
  -> in-process command dispatch
  -> SagaInboxCommandScopePreHandler (nested scope re-attach)
  -> ICommandHandler<TCommand> uses ISagaContext
  -> SagaProcessorHook.AfterDispatchAsync (save or complete)
  -> inbox terminal status persistence
```

## Package Map

| Package | Layer | Why it exists |
| --- | --- | --- |
| `LiteBus.Orchestration.Abstractions` | 1 | Hook contract (`IProcessorEnvelopeHook`, `IProcessorEnvelope`) shared by inbox and outbox processors |
| `LiteBus.Saga.Abstractions` | 1 | Saga contracts (`ISagaStore`, `ISagaContext`, `SagaCorrelation`, query and purge filters) |
| `LiteBus.Saga` | 2 | Core saga runtime (`SagaProcessorHook`, message-keyed `SagaExecutionContext`, `SagaStateTypeRegistry`, `InMemorySagaStore`) |
| `LiteBus.Saga.InboxIntegration` | 4 | Inbox builder entry point (`EnableSaga`) and command pre-handler module |
| `LiteBus.Saga.Storage.PostgreSql` | 4 | PostgreSQL `ISagaStore`, schema scripts, startup initializer |

## Typical Composition Recipe

```csharp
services.AddLiteBus(registry =>
{
    registry.AddInboxModule(inbox =>
    {
        inbox.Contracts.Register<AdvanceOrderSagaCommand>("orders.saga.advance");
        inbox.UseInProcessDispatch();
        inbox.EnableSaga(saga => saga.MapState<OrderSagaState>("orders.saga.advance"));
        // Optional durable store:
        // inbox.UsePostgreSqlSagaStorage(pg => pg.UseDataSource(dataSource));
    });
});
```

## Capability Pages

| ID | Page |
| --- | --- |
| `saga.processor-envelope-hooks` | [processor-envelope-hooks](processor-envelope-hooks.md) |
| `saga.processor-hook` | [processor-hook](processor-hook.md) |
| `saga.handler-context` | [handler-context](handler-context.md) |
| `saga.state-registration` | [state-registration](state-registration.md) |
| `saga.inbox-integration` | [inbox-integration](inbox-integration.md) |
| `saga.inbox-command-scope` | [inbox-command-scope](inbox-command-scope.md) |
| `saga.correlation-and-tenancy` | [correlation-and-tenancy](correlation-and-tenancy.md) |
| `saga.store` | [store](store.md) |
| `saga.in-memory-store` | [in-memory-store](in-memory-store.md) |
| `saga.postgresql-storage` | [postgresql-storage](postgresql-storage.md) |
| `saga.optimistic-concurrency` | [optimistic-concurrency](optimistic-concurrency.md) |

## Test Executor Summary

| Test executor | Project and path | Coverage focus |
| --- | --- | --- |
| Unit | `tests/LiteBus.Storage.UnitTests/Saga/` | Hook phases, same-correlation scope isolation, registry resolution, in-memory concurrency, query, purge, and schema bootstrap |
| Integration | `tests/LiteBus.Storage.IntegrationTests/PostgreSql/` | End-to-end inbox plus saga plus PostgreSQL, orchestration depth, compensation, optimistic concurrency, connection ownership |
| Composition smoke | `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/` | v6 composition wiring (`EnableSaga`) and correlated state persistence |

## Key Gaps Documented

| Gap | Status in v6 |
| --- | --- |
| Outbox saga support | Not implemented |
| Single transaction for inbox terminal plus saga save | Not implemented |
| Built-in scheduler, timeout orchestration, compensation DSL | Not implemented |
| EF Core saga store adapter | Not implemented |
| Saga-specific metrics and activity source | Not implemented |
| Automatic correlation id generation | Not implemented |
| Saga metrics and tracing | Use inbox processor signals; no saga-specific instruments in v6 |

## Deep Docs

- [Architecture](../../architecture/README.md)
- [Dependency graph](../../architecture/dependency-graph.md)
- [Roadmap](../../roadmap/README.md)
- [Cookbook and scenarios](../../getting-started/cookbook.md)
