# Handler Pipeline

- **ID**: `mediator.handler-pipeline`
- **Name**: Handler pipeline
- **Maturity**: GA
- **Summary**: Defines pre, main, post, and error stage execution for command, query, and event mediation.

## What It Does

The pipeline is shared across semantic mediators:
1. Pre-handlers.
2. Main handler(s).
3. Post-handlers.
4. Error-handlers on recoverable failures.

Direct and indirect handlers run in a fixed order:
- Pre: indirect then direct.
- Post: direct then indirect.
- Error: indirect then direct.

Single-handler strategies (commands and queries) support `Abort(...)`. Broadcast strategy (events) uses its own fan-out flow and does not use abort as a success path.

## Public Surface

```csharp
public sealed class AuditPreHandler : ICommandPreHandler<CreateOrderCommand>
{
    public Task PreHandleAsync(CreateOrderCommand message, CancellationToken cancellationToken = default)
    {
        AmbientExecutionContext.Current.Items["trace"] = "pre-stage";
        return Task.CompletedTask;
    }
}
```

| API | Role |
| --- | --- |
| `IAsyncMessagePreHandler<TMessage>` | Generic pre stage contract |
| `IAsyncMessageHandler<TMessage>` / `IAsyncMessageHandler<TMessage, TResult>` | Main handler contracts |
| `IAsyncMessagePostHandler<TMessage>` / `IAsyncMessagePostHandler<TMessage, TResult>` | Post stage contracts |
| `IAsyncMessageErrorHandler<TMessage, TResult>` | Error stage contract |
| `SingleAsyncHandlerMediationStrategy<TMessage, TResult>` | Single main handler orchestration |
| `SingleStreamHandlerMediationStrategy<TMessage, TResult>` | Stream query orchestration |
| `AsyncBroadcastMediationStrategy<TMessage>` | Event broadcast orchestration |
| `MessageContextExtensions.RunAsyncPreHandlers/RunAsyncPostHandlers/RunAsyncErrorHandlers` | Stage execution helpers |

## Packages

- `LiteBus.Messaging`
- `LiteBus.Messaging.Abstractions`

## Requires

- `mediator.execution-context`
- `mediator.handler-priority`
- `mediator.handler-filtering`

## Invariants

- Single-handler strategies resolve exactly one main handler.
- Post-handler result override uses `executionContext.MessageResult`.
- Recoverable exceptions route to error handlers; cancellation and abort follow dedicated semantics.
- Event broadcast may execute handlers concurrently based on event execution settings.

## Non-Goals

- Automatic retries or delayed re-execution on failures.
- Transaction boundaries across multiple handlers.
- Pipeline-level persistence of stage artifacts.

## Observability

No pipeline-specific meter, activity source, or structured event catalog is exposed in mediator packages.

Operational alternatives:
- Add application logs in pre/post/error handlers.
- Add custom timing in handlers using `AmbientExecutionContext.Items`.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Send_CreateProductCommand_ShouldGoThroughHandlersCorrectly` | `LiteBus.Mediator.UnitTests` |
| `Mediating_GetProductQuery_ShouldGoThroughHandlersCorrectly` | `LiteBus.Mediator.UnitTests` |
| `mediating_event_with_exception_in_main_handler_goes_through_error_handlers` | `LiteBus.Mediator.UnitTests` |
| `mediating_a_command_that_is_aborted_in_pre_handler_goes_through_correct_handlers` | `LiteBus.Mediator.UnitTests` |
| `Send_CommandWithResult_PostHandlerOverridesResult` | `LiteBus.Mediator.UnitTests` |

### Untested

- Deep nested indirect handler chains mixed with large tag filters and predicate filters.
- Pipeline memory behavior for extremely long stream lifetimes.

### Out-of-Scope

- Broker transport ingress/dispatch pipelines.
- Durable inbox/outbox processor pipeline behaviors.

## Deep Docs

- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Execution context](../../concepts/execution-context.md)
