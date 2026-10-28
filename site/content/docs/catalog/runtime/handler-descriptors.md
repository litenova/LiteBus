# Handler Descriptor Model

- **ID**: `runtime.handler-descriptors`
- **Name**: Handler descriptor model
- **Maturity**: GA
- **Summary**: The descriptor contracts describe message handlers: their stages, priorities, tags, registration sequence, and the closed contract each handler was discovered from.

## What It Does

Descriptor contracts in `LiteBus.Messaging.Abstractions` connect registration and runtime execution. `IMessageDescriptor` groups handlers by stage and by direct or indirect applicability, and exposes the declarative metadata resolved for the message type. `IHandlerDescriptor` provides the metadata that filtering and ordering use, and the closed contract the handler was discovered from.

Because the descriptor records the contract, the pipeline invokes a handler through the contract it was registered under instead of searching its interfaces at dispatch. For the pre, post, completion, and refusal-mapper stages the descriptor also carries a `PipelineDispatch`, a bound dispatch holder for that contract that is built together with the descriptor. Dispatch then reads a field instead of using reflection.

The pipeline invokes main handlers through the same recorded contract. `HandlerInvocation` calls the strategy-typed contract directly when the descriptor names the strategy message type. Otherwise the internal `MainHandlerDispatch.Invoke` binds the recorded closed contract, so an event published as `IEvent` still reaches its concrete registration with the mediation cancellation token.

## Public Surface

| API | Role |
| --- | --- |
| `IMessageDescriptor` | Message descriptor with stage-specific handler collections |
| `IHandlerDescriptor` | Base handler metadata contract |
| `IMainHandlerDescriptor` | Main handler descriptor |
| `IPreStageHandlerDescriptor` | Pre-stage descriptor, shared by guards, validators, shortcuts, and pre-handlers |
| `IPostHandlerDescriptor` | Post-handler descriptor |
| `ICompletionHandlerDescriptor` | Completion handler descriptor |
| `IErrorHandlerDescriptor` | Error-handler descriptor |
| `IRefusalMapperDescriptor` | Refusal mapper descriptor |
| `MainHandlerDispatch.Invoke` (internal) | Invokes a main handler through the closed contract on its descriptor |
| `IHandlerDescriptor.ContractType` | The closed contract the descriptor was discovered from |
| `PipelineDispatch` | Bound dispatch holder for that contract, built at registration |
| `IMessageDescriptor.Metadata` | Declarative metadata resolved for the message type |

## Packages

- `LiteBus.Messaging.Abstractions`

## Requires

- `runtime.message-registry`
- `runtime.mediation-strategies`

## Invariants

- `RegistrationSequence` is a stable registration-order tiebreaker.
- The registry normalizes descriptor message types for generics.
- `ContractType` is open only when the handler was registered for a generic message. The pipeline binds those on first dispatch.
- On descriptors that carry `Dispatch` (pre, post, completion), it is null exactly when `ContractType` is open.

## Non-Goals

- A format for storing descriptors persistently.

## Observability

Descriptors emit no telemetry. Their effects are visible through mediation behavior.

## Test Coverage

### Covered Use Cases

#### `MessageRegistryTests.Register_Handler_ShouldRegisterHandlerAndMessage`
- **Test kind**: Unit
- **Expected outcome**: the registry creates and links the descriptors

#### `PredicateFilteringTests.Publish_Event_WithPredicate_ShouldExecuteOnlyMatchingHandlers`
- **Test kind**: Unit
- **Expected outcome**: only the matching handlers execute under descriptor-based predicate filtering

### Untested Use Cases

| Use case | Priority | Notes |
| --- | --- | --- |
| Heavy tag-collision scenarios | Low | Core filter behavior is covered |

### Out-of-Scope Use Cases

- Generating descriptors without reflection.
