# Core Message Mediator

- **ID**: `runtime.message-mediator`
- **Name**: Core message mediator
- **Maturity**: GA
- **Summary**: Resolves message descriptors and executes mediation strategies within per-call execution and dispatch scopes.

## What It Does

`MessageMediator` powers semantic mediators. It creates execution context, creates dispatch scope, resolves descriptors, optionally registers plain messages on the spot, runs strategy execution, and retains scopes for async completion.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageMediator.Mediate<TMessage,TResult>(...)` | Main mediation entry |
| `IMessageMediator.MediateAsync<TMessage,TResult>(...)` | Async wrapper |
| `MessageMediationRequest<TMessage,TResult>` | Resolve strategy, mediation strategy, tags, predicate, items |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.message-registry`
- `runtime.message-resolution`
- `runtime.mediation-strategies`
- `runtime.dispatch-scopes`

## Invariants

- Each mediation call creates one dispatch scope.
- Missing descriptor after retry path throws `MessageDescriptorNotFoundException`.
- No handler with on-spot disabled throws `NoHandlerFoundException`.

## Non-Goals

- Cross-process mediation.

## Observability

No dedicated mediator meter in runtime core.

## Test Coverage

### Covered Use Cases

#### `MessageMediatorTests.Mediate_WhenDescriptorCannotBeResolvedAfterOnTheSpotRegistration_ShouldThrowMessageDescriptorNotFoundException`
- **Test kind**: Unit
- **Expected outcome**: unresolved descriptor path throws expected exception

#### `MediateAsyncTests.MediateAsync_WhenStrategyReturnsTask_ShouldReturnScopeRetainedTask`
- **Test kind**: Unit
- **Expected outcome**: the returned task completes after the retained dispatch scope is released

#### `MediationScopeRetentionTests.Mediate_delayed_task_retains_dispatch_scope_until_task_completes`
- **Test kind**: Unit
- **Expected outcome**: scope remains alive until async completion

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Sustained throughput with aggressive on-spot plain message registration | Medium | Functional paths are covered |

### Out-of-Scope Use Cases

- Automatic transport fallback on handler absence.
