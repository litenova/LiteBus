# Per-Mediation DI Scopes

- **ID**: `runtime.dispatch-scopes`
- **Name**: Per-mediation DI scopes
- **Maturity**: GA
- **Summary**: Each mediation call runs in an isolated service-provider scope created through `IMessageDispatchScopeFactory`.

## What It Does

The Microsoft DI and Autofac adapters each register one container-specific `IMessageDispatchScopeFactory`. `MessageModule` requires that contract: it never looks for a container itself and never falls back to the root provider on its own.

`MessageMediator` creates one dispatch scope per call and retains it until async work completes. A manual host that intends root-provider resolution registers `RootMessageDispatchScopeFactory` explicitly.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageDispatchScopeFactory.CreateScope()` | Creates one dispatch scope |
| `IMessageDispatchScope.ServiceProvider` | Service provider for handler resolution |
| `LazyMessageDispatchScope` (internal) | Defers host scope creation until the first service resolution, so an unenumerated stream holds no container scope |
| Microsoft DI dispatch-scope factory | Opens an `IServiceScope` per dispatch |
| Autofac dispatch-scope factory | Opens an `ILifetimeScope` per dispatch |
| `RootMessageDispatchScopeFactory` | Explicit root-provider opt-in |

## Packages

- `LiteBus.Runtime.Abstractions` for contracts
- `LiteBus.Runtime` for explicit root-provider implementation
- `LiteBus.Runtime.Extensions.Microsoft.DependencyInjection` or `LiteBus.Runtime.Extensions.Autofac` for container implementations
- `LiteBus.Messaging` for the consumer

## Requires

- `runtime.message-module`
- `runtime.message-mediator`

## Invariants

- Each mediation operation creates one dispatch scope.
- Disposing the scope disposes the scoped provider.
- Composition without a scope factory fails before the container is used.
- Explicit root dispatch does not dispose the root container.

## Non-Goals

- Sharing one scope across messages.

## Observability

The runtime exposes no metric for dispatch scopes.

## Test Coverage

### Covered Use Cases

#### `MediationScopeRetentionTests.Mediate_delayed_task_retains_dispatch_scope_until_task_completes`
- **Test kind**: Unit
- **Expected outcome**: the mediator retains the scope until the delayed task completes

#### `MediationScopeRetentionTests.Mediate_stream_result_retains_dispatch_scope_until_enumeration_completes`
- **Test kind**: Unit
- **Expected outcome**: the mediator retains the scope for the full stream enumeration

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Manual-host root opt-in across a complete mediator pipeline | Medium | The explicit factory is unit-covered, but host-specific composition remains application-owned |

### Out-of-Scope Use Cases

- Orchestrating nested scopes in the core runtime.
