# Pluggable Mediation Strategies

- **ID**: `runtime.mediation-strategies`
- **Name**: Pluggable mediation strategies
- **Maturity**: GA
- **Summary**: Defines strategy contracts and default single-main-handler task and stream strategies.

## What It Does

Strategies implementing `IMessageMediationStrategy<TMessage,TResult>` control pre, main, post, error, and completion handler execution. The completion stage runs in a `finally`, so every strategy reports an outcome even when the pipeline aborts, fails, or is cancelled. Runtime ships single-main-handler strategies for `Task`, `Task<T>`, and `IAsyncEnumerable<T>`.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageMediationStrategy<TMessage,TResult>` | Strategy contract |
| `SingleAsyncHandlerMediationStrategy<TMessage>` | Task strategy |
| `SingleAsyncHandlerMediationStrategy<TMessage,TResult>` | Task result strategy |
| `SingleStreamHandlerMediationStrategy<TMessage,TResult>` | Stream strategy |
| `SingleMainHandlerResolver.Resolve<TMessage>(...)` | Enforces single-main-handler rule |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.handler-descriptors`
- `runtime.message-registry`

## Invariants

- Direct main handlers are preferred over indirect handlers.
- Zero or multiple main handlers fail with explicit exceptions.
- Recoverable exceptions invoke error handlers.

## Non-Goals

- Built-in parallel multi-main-handler strategies.

## Observability

No dedicated strategy telemetry.

## Test Coverage

### Covered Use Cases

#### `MediationCorrectnessTests.Send_Command_ShouldRetainAmbientScopeUntilHandlerContinuationCompletes`
- **Test kind**: Unit
- **Expected outcome**: strategy preserves ambient execution semantics

#### `PostHandlerResultOverrideTests.Send_CommandWithMultiplePostHandlers_LastWriteWins`
- **Test kind**: Unit
- **Expected outcome**: post-handler override semantics are respected

#### `MediationScopeRetentionTests.Mediate_stream_result_retains_dispatch_scope_until_enumeration_completes`
- **Test kind**: Unit
- **Expected outcome**: stream strategy retains scope through enumeration

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Custom non-task result wrappers | Medium | Contract supports custom strategies, default suite targets shipped ones |

### Out-of-Scope Use Cases

- Parallel fan-out strategy implementation in core runtime package.
