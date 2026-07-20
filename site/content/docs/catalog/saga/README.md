# Saga Capability Catalog

LiteBus saga support is an Extension tier feature. It adds correlated state persistence around inbox command dispatch through `IProcessorEnvelopeHook` and `ISagaContext`.

Saga registration is composed from the inbox axis. Register saga through `builder.AddInbox(inbox => inbox.EnableSaga(...))` and select exactly one store in the saga callback. There is no public top-level `AddSagaModule(...)` API.

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

| Package | Role | Why it exists |
| --- | --- | --- |
| `LiteBus.DurableMessaging.Abstractions` | Durable contracts | Hook contract (`IProcessorEnvelopeHook`, `IProcessorEnvelope`) shared by inbox and outbox processors |
| `LiteBus.Saga.Abstractions` | Durable contracts | Saga contracts (`ISagaStore`, `ISagaContext`, `SagaCorrelation`, query and purge filters) |
| `LiteBus.Saga` | Core implementation | Saga runtime, nested builder, and explicit in-memory storage module |
| `LiteBus.Saga.InboxIntegration` | Feature bridge | Inbox builder entry point (`EnableSaga`) and command pre-handler module |
| `LiteBus.Saga.Storage.PostgreSql` | Feature bridge | PostgreSQL `ISagaStore`, schema scripts, startup initializer |

## Typical Composition Recipe

```csharp
services.AddLiteBus(builder =>
{
    builder.AddMessaging(_ => { });
    builder.AddInbox(inbox =>
    {
        inbox.Contracts.Register<AdvanceOrderSagaCommand>("orders.saga.advance");
        inbox.UseInProcessDispatch();
        inbox.EnableSaga(saga =>
        {
            saga.MapState<OrderSagaState>("orders.saga.advance");
            saga.UseInMemoryStorage();
        });
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
| Composition smoke | `tests/LiteBus.Runtime.UnitTests/Runtime/Composition/` | `EnableSaga` composition wiring and correlated state persistence |

## Key Gaps Documented

| Gap | Current Status |
| --- | --- |
| Outbox saga support | Not implemented |
| Single transaction for inbox terminal plus saga save | Not implemented |
| Built-in scheduler, timeout orchestration, compensation DSL | Not implemented |
| EF Core saga store adapter | Not implemented |
| Saga-specific metrics and activity source | Not implemented |
| Automatic correlation id generation | Not implemented |
| Saga metrics and tracing | Use inbox processor signals; there are no saga-specific instruments |

## Deep Docs

- [Architecture](../../architecture/README.md)
- [Dependency graph](../../architecture/dependency-graph.md)
- [Roadmap](../../roadmap/README.md)
- [Cookbook and scenarios](../../getting-started/cookbook.md)
