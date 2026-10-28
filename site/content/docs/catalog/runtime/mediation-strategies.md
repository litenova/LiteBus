# Pluggable Mediation Strategies

- **ID**: `runtime.mediation-strategies`
- **Name**: Pluggable mediation strategies
- **Maturity**: GA
- **Summary**: The runtime defines the strategy contracts and the default strategies: single-main-handler strategies for task and stream results.

## What It Does

Strategies implementing `IMessageMediationStrategy<TMessage,TResult>` control pre, main, post, error, and completion handler execution. In the execution strategies LiteBus ships, the completion stage runs in a `finally` block, so every run reports an outcome even when the pipeline aborts, fails, or is cancelled. The runtime ships single-main-handler strategies for `Task`, `Task<T>`, and `IAsyncEnumerable<T>`.

Decision evaluation is a strategy, not a mediation. `DecisionEvaluationMediationStrategy<TMessage>` implements `IMessageMediationStrategy<TMessage, Task<MediationDecision>>` for the `Evaluate` methods. It runs guards, then validators, in that fixed order, and then stops. No shortcut claims a key, no pre-handler or main handler runs, the completion stage does not run, and no audit record is produced. A fault that a refusal classifier recognizes is reported as the refusal the message would end with if actually sent.

Event fan-out uses `AsyncBroadcastMediationStrategy`, which [mediator.events](../mediator/events.md) covers.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageMediationStrategy<TMessage,TResult>` | Strategy contract |
| `SingleAsyncHandlerMediationStrategy<TMessage>` | Task strategy |
| `SingleAsyncHandlerMediationStrategy<TMessage,TResult>` | Task result strategy |
| `SingleStreamHandlerMediationStrategy<TMessage,TResult>` | Stream strategy |
| `DecisionEvaluationMediationStrategy<TMessage>` | Decision-stages-only strategy backing `Evaluate` |
| `SingleMainHandlerResolver` (internal) | Enforces single-main-handler rule |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.handler-descriptors`
- `runtime.message-registry`

## Invariants

- The resolver prefers direct main handlers over indirect handlers.
- When zero or multiple main handlers match, resolution throws an explicit exception.
- When a handler throws a recoverable exception, the pipeline invokes error handlers.

## Non-Goals

- Parallel strategies with multiple main handlers built into the runtime.

## Observability

Strategies emit no telemetry.

## Test Coverage

### Covered Use Cases

#### `MediationCorrectnessTests.Send_Command_ShouldRetainAmbientScopeUntilHandlerContinuationCompletes`
- **Test kind**: Unit
- **Expected outcome**: the strategy preserves ambient execution semantics

#### `PostHandlerResultOverrideTests.Send_CommandWithMultiplePostHandlers_LastWriteWins`
- **Test kind**: Unit
- **Expected outcome**: the pipeline respects post-handler override semantics

#### `MediationScopeRetentionTests.Mediate_stream_result_retains_dispatch_scope_until_enumeration_completes`
- **Test kind**: Unit
- **Expected outcome**: the stream strategy retains the scope through enumeration

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Custom non-task result wrappers | Medium | The contract supports custom strategies, but the default suite targets the shipped ones |

### Out-of-Scope Use Cases

- A parallel fan-out strategy implemented in the core runtime package.
