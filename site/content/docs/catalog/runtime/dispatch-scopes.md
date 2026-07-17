# Per-Mediation DI Scopes

- **ID**: `runtime.dispatch-scopes`
- **Name**: Per-mediation DI scopes
- **Maturity**: GA
- **Summary**: Creates isolated service-provider scopes per mediation call through `IMessageDispatchScopeFactory`.

## What It Does

The Microsoft DI and Autofac adapters each register one container-specific `IMessageDispatchScopeFactory`. `MessageModule` requires that contract and does not inspect a container or silently fall back to the root provider. `MessageMediator` creates one dispatch scope per call and retains it through async completion. Manual hosts may explicitly register `RootMessageDispatchScopeFactory` when root-provider resolution is intentional.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageDispatchScopeFactory.CreateScope()` | Creates one dispatch scope |
| `IMessageDispatchScope.ServiceProvider` | Service provider for handler resolution |
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

- One dispatch scope per mediation operation.
- Scoped provider is disposed on scope dispose.
- Missing scope-factory composition fails before the container is used.
- Explicit root dispatch does not dispose the root container.

## Non-Goals

- Cross-message shared scopes.

## Observability

No dedicated scope metric.

## Test Coverage

### Covered Use Cases

#### `MediationScopeRetentionTests.Mediate_delayed_task_retains_dispatch_scope_until_task_completes`
- **Test kind**: Unit
- **Expected outcome**: scope retained until delayed task completion

#### `MediationScopeRetentionTests.Mediate_stream_result_retains_dispatch_scope_until_enumeration_completes`
- **Test kind**: Unit
- **Expected outcome**: scope retained for full stream enumeration

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Manual-host root opt-in across a complete mediator pipeline | Medium | The explicit factory is unit-covered; host-specific composition remains application-owned |

### Out-of-Scope Use Cases

- Nested scope orchestration in core runtime.
