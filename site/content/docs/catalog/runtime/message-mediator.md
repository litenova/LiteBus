# Core Message Mediator

- **ID**: `runtime.message-mediator`
- **Name**: Core message mediator
- **Maturity**: GA
- **Summary**: The mediator resolves message descriptors and executes mediation strategies within per-call execution and dispatch scopes.

## What It Does

`MessageMediator` powers semantic mediators. For each call it:

1. Creates the execution context and the dispatch scope.
2. Resolves the message descriptors.
3. Optionally registers plain messages on the spot.
4. Runs the strategy.
5. Retains the scopes until async work completes.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageMediator.Mediate<TMessage,TResult>(...)` | Main mediation entry |
| `MessageMediationRequest<TMessage,TResult>` | Carries the resolve strategy, mediation strategy, tags, predicate, items, and on-spot registration |

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
- `Mediate` is synchronous and returns the strategy result directly. Scope retention keeps the dispatch scope alive until an async result completes.
- When the descriptor is still missing after the retry path, mediation throws `MessageDescriptorNotFoundException`.
- When no handler exists and on-spot registration is disabled, mediation throws `NoHandlerFoundException`.

## Non-Goals

- Mediating across processes.

## Observability

The mediator exposes no meter in the runtime core.

## Test Coverage

### Covered Use Cases

#### `MessageMediatorTests.Mediate_WhenDescriptorCannotBeResolvedAfterOnTheSpotRegistration_ShouldThrowMessageDescriptorNotFoundException`
- **Test kind**: Unit
- **Expected outcome**: the unresolved descriptor path throws the expected exception

#### `MediationScopeRetentionTests.Mediate_delayed_task_retains_dispatch_scope_until_task_completes`
- **Test kind**: Unit
- **Expected outcome**: the scope remains alive until async completion

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Sustained throughput with aggressive on-spot plain message registration | Medium | Functional paths are covered |

### Out-of-Scope Use Cases

- Falling back to a transport automatically when no handler exists.
