# Per-Mediation DI Scopes

- **ID**: `runtime.dispatch-scopes`
- **Name**: Per-mediation DI scopes
- **Maturity**: GA
- **Summary**: Creates isolated service-provider scopes per mediation call through `IMessageDispatchScopeFactory`.

## What It Does

`MessageModule` registers `IMessageDispatchScopeFactory` that chooses scoped-provider mode when `IServiceScopeFactory` exists, or root-provider fallback otherwise. `MessageMediator` creates one dispatch scope per call and retains it through async completion.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageDispatchScopeFactory.CreateScope()` | Creates one dispatch scope |
| `IMessageDispatchScope.ServiceProvider` | Service provider for handler resolution |
| `MessageDispatchScopeFactory` | Scoped-provider implementation |
| `RootMessageDispatchScopeFactory` | Root-provider fallback |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.message-module`
- `runtime.message-mediator`

## Invariants

- One dispatch scope per mediation operation.
- Scoped provider is disposed on scope dispose.
- Root fallback does not dispose root container.

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
| End-to-end root-fallback path without `IServiceScopeFactory` | Medium | Fallback is source-verified; explicit end-to-end test is limited |

### Out-of-Scope Use Cases

- Nested scope orchestration in core runtime.
