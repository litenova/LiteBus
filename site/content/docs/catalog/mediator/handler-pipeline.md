# Handler Pipeline

- **ID**: `mediator.handler-pipeline`
- **Name**: Handler pipeline
- **Maturity**: GA
- **Summary**: Defines pre, main, post, error, and completion stage execution for command, query, and event mediation.

## What It Does

The pipeline is shared across semantic mediators:
1. Pre-handlers.
2. Main handler(s).
3. Post-handlers.
4. Error-handlers on recoverable failures.
5. Completion handlers on every outcome path.

Direct and indirect handlers run in a fixed order:
- Pre: indirect then direct.
- Post: direct then indirect.
- Error: indirect then direct.
- Completion: direct then indirect.

Single-handler strategies (commands and queries) support short-circuiting from a pre-handler through `PipelineDirective`, optionally with a reason surfaced to completion handlers. Events have no short-circuiting contract. Any stage may call `IExecutionContext.SuppressPostHandlers()` to skip the remaining post-handlers without changing the outcome.

The completion stage runs in a `finally` inside the ambient execution scope, so it observes success, abort, failure, and cancellation alike. It is the only stage guaranteed to run.

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
| `MessageErrorContext<TMessage, TResult>` | Typed error data and shared recovery outcome |
| `IMessageCompletionHandler` / `IMessageCompletionHandler<TMessage>` | Completion stage contracts |
| `MessageCompletionContext` / `MessageCompletionContext<TMessage>` | Read-only outcome, result, exception, abort reason, duration |
| `MessageOutcome` | `Succeeded`, `Aborted`, `Failed`, `Canceled` |
| `LiteBusHandlerPriority` | Reserved priority band for handlers shipped by LiteBus |
| `SingleAsyncHandlerMediationStrategy<TMessage, TResult>` | Single main handler orchestration |
| `SingleStreamHandlerMediationStrategy<TMessage, TResult>` | Stream query orchestration |
| `AsyncBroadcastMediationStrategy<TMessage>` | Event broadcast orchestration |
| `MessageContextExtensions.RunAsyncPreHandlers/RunAsyncPostHandlers/RunAsyncErrorHandlers/RunAsyncCompletionHandlers` | Stage execution helpers |

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
- Recoverable exceptions route to error handlers with the caller's explicit cancellation token.
- Error handlers suppress the original exception only by setting their shared context outcome to `Handled`.
- Event broadcast may execute handlers concurrently based on event execution settings.
- Completion handlers run exactly once per mediation, on every outcome path.
- Completion handlers cannot change the outcome; the context is read-only.
- A completion handler that throws is suppressed when the mediation already faulted, and propagates when it succeeded.
- Stream completion fires on enumerator disposal, so an unenumerated stream produces no completion record.

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
| `Send_Command_WithErrorHandler_ShouldPassTypedContextAndExplicitCancellationToken` | `LiteBus.Mediator.UnitTests` |
| `Send_Command_WithObservingErrorHandler_ShouldRethrowByDefault` | `LiteBus.Mediator.UnitTests` |
| `Send_CommandWithResult_WhenErrorHandlerSetsHandledResult_ShouldReturnFallbackResult` | `LiteBus.Mediator.UnitTests` |
| `Completion_runs_with_Succeeded_when_the_handler_succeeds` | `LiteBus.Mediator.UnitTests` |
| `Completion_runs_with_Failed_when_the_handler_throws` | `LiteBus.Mediator.UnitTests` |
| `Completion_runs_with_Aborted_and_carries_the_reason` | `LiteBus.Mediator.UnitTests` |
| `Completion_runs_with_Canceled_and_the_cancellation_still_propagates` | `LiteBus.Mediator.UnitTests` |
| `Direct_completion_handlers_run_before_indirect_ones` | `LiteBus.Mediator.UnitTests` |
| `A_failing_completion_handler_does_not_replace_the_original_fault` | `LiteBus.Mediator.UnitTests` |

### Untested

- Deep nested indirect handler chains mixed with large tag filters and predicate filters.
- Pipeline memory behavior for extremely long stream lifetimes.

### Out-of-Scope

- Broker transport ingress/dispatch pipelines.
- Durable inbox/outbox processor pipeline behaviors.

## Deep Docs

- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Execution context](../../concepts/execution-context.md)
- [Message definitions](../../concepts/message-definitions.md)
- [Auditing](../../concepts/auditing.md)
