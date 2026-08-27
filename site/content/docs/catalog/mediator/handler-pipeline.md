# Handler Pipeline

- **ID**: `mediator.handler-pipeline`
- **Name**: Handler pipeline
- **Maturity**: GA
- **Summary**: Defines pre, main, post, error, and completion stage execution for command, query, and event mediation. The pre stage holds four framework-ordered roles: guard, validator, shortcut, and pre-handler.

## What It Does

The pipeline is shared across semantic mediators:
1. Pre-handlers.
2. Main handler(s).
3. Post-handlers.
4. Error-handlers on recoverable failures.
5. Completion handlers on every outcome path.

Direct and indirect handlers run in a fixed order:
- Guard, shortcut, and pre: indirect then direct.
- Post: direct then indirect.
- Error: indirect then direct.
- Completion: direct then indirect.

The pre stage is four framework-ordered stages, not one. Guards run first and refuse through `Verdict`; validators run next and report failures through `Validity`; shortcuts run third and answer through `Shortcut` or `Shortcut<TResult>`; plain pre-handlers run last and cannot stop the pipeline by returning. Priority orders handlers inside a stage and never reorders the stages, so a globally registered shortcut cannot answer ahead of a message-specific guard, and a malformed message cannot claim an idempotency key. Guards and shortcuts stop at the first decision; the validator stage runs every validator and collects their failures. A refusal with no registered `IMessageRefusalMapper<TMessage, TMessageResult>` raises `LiteBusMessageDeniedException` or `LiteBusMessageInvalidException`, both excluded from the recoverable filter so error handlers never see a decision as a fault. Any stage may call `IExecutionContext.SuppressPostHandlers()` to skip the remaining post-handlers without changing the outcome.

The completion stage runs in a `finally` inside the ambient execution scope, so it observes success, an early answer, a denial, a validation failure, a fault, and cancellation alike. It is the only stage guaranteed to run, and it is not cancellable.

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
| `IMessagePreHandler<TMessage>` | Pre stage contract |
| `IMessageGuard<TMessage>` | Guard stage contract; refuses the message |
| `IMessageValidator<TMessage>` | Validator stage contract; reports the message malformed |
| `IMessageRefusalMapper<TMessage, TResult>` | Turns a refusal into the value the caller receives |
| `IMessageShortcut<TMessage>` / `IMessageShortcut<TMessage, TResult>` | Shortcut stage contracts that may answer without the handler |
| `Verdict` | Allow or deny, with a reason and an optional code |
| `Validity` / `ValidationFailure` | Valid, or the failures a validator reported |
| `Refusal` | The outcome, reason, and code handed to a refusal mapper |
| `Shortcut` / `Shortcut<TResult>` | No answer, skip, or answer, with a reason and a result |
| `PipelineStage` | `Guard`, `Validator`, `Shortcut`, `PreHandler`; the framework-fixed stage order |
| `PipelineStop` | What a stage reported: outcome, reason, and the result the caller receives |
| `LiteBusMessageDeniedException` | Raised when a refusal supplies no result for the caller |
| `IAsyncMessageHandler<TMessage>` / `IAsyncMessageHandler<TMessage, TResult>` | Main handler contracts |
| `IMessagePostHandler<TMessage, TResult>` | Post stage contract |
| `IExecutionContext.SuppressPostHandlers()` | Skips the post-handlers that have not run yet |
| `IMessageErrorHandler<TMessage, TResult>` | Error stage contract |
| `MessageErrorContext<TMessage, TResult>` | Typed error data and shared recovery outcome |
| `IMessageCompletionHandler` / `IMessageCompletionHandler<TMessage>` / `IMessageCompletionHandler<TMessage, TResult>` | Completion stage contracts |
| `MessageCompletionContext` and its typed views | Read-only outcome, result, exception, reason, duration |
| `MessageOutcome` | `Succeeded`, `Answered`, `Denied`, `Invalid`, `Failed`, `Canceled` |
| `MediationExceptionData.SuppressedCompletionFaults` | Key under which a suppressed completion fault is attached to the original exception |
| `HandlerPriorities` | Reserved priority band for handlers shipped by LiteBus |
| `SingleAsyncHandlerMediationStrategy<TMessage, TResult>` | Single main handler orchestration |
| `SingleStreamHandlerMediationStrategy<TMessage, TResult>` | Stream query orchestration |
| `AsyncBroadcastMediationStrategy<TMessage>` | Event broadcast orchestration |
| `MessageContextExtensions.RunAsyncPreStages/RunAsyncGuards/RunAsyncValidators/RunAsyncShortcuts/RunAsyncPreHandlers/RunAsyncPostHandlers/RunAsyncErrorHandlers/RunAsyncCompletionHandlers` | Stage execution helpers |
| `MessageContextExtensions.ResolveRefusalResult<TMessageResult>` | Applies the registered refusal mapper, or raises when none covers the message |
| `PipelineDispatch` | Delegate bound at registration to the closed contract a handler was discovered from, and `StageFor` |
| `IPreHandlerDescriptor.Stage` | The stage that runs a discovered pre-stage handler |
| `IMessageDependencies.HasPreStageHandlers` | Whether a stage holds any handler, so an empty one costs nothing |
| `MediationOutcome` | Tracks how a mediation ended and reports it to the completion stage |
| `IRefusalMapperDescriptor` | A registered refusal mapper and the result type it produces |
| `IHandlerDescriptor.ContractType` | The closed contract a descriptor was discovered from |

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
- Completion handlers receive `CancellationToken.None`; the stage runs to the end on every path.
- A completion handler that throws while an exception is ending the mediation has its fault attached to that exception under `MediationExceptionData.SuppressedCompletionFaults`, and propagates otherwise.
- Stream completion fires on enumerator disposal, so an unenumerated stream produces no completion record.
- Only a guard, a validator, or a shortcut can stop the pipeline; `Answered`, `Denied`, and `Invalid` all mean the main handler never ran.
- A denial is not routed to error handlers and is not reported as `Faulted`.
- Suppressing post-handlers reports `MessageOutcome.Succeeded`, because the main handler ran.
- A shortcut that answers a result-returning message must supply a result of the expected type, or mediation throws `LiteBusConfigurationException`. Analyzer rule `LB1019` reports a shortcut that used the untyped contract for such a message. A guard has no such duty, so the untyped guard is correct everywhere.
- One class may implement pipeline contracts for several message types; each dispatch reaches the contract recorded in its descriptor.

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
| `mediating_a_command_answered_by_a_shortcut_goes_through_correct_handlers` | `LiteBus.Mediator.UnitTests` |
| `A_guard_runs_before_a_shortcut_even_when_scope_and_priority_favour_the_shortcut` | `LiteBus.Mediator.UnitTests` |
| `The_four_stages_run_as_guards_then_validators_then_shortcuts_then_pre_handlers` | `LiteBus.Mediator.UnitTests` |
| `Send_CommandWithResult_PostHandlerOverridesResult` | `LiteBus.Mediator.UnitTests` |
| `Send_Command_WithErrorHandler_ShouldPassTypedContextAndExplicitCancellationToken` | `LiteBus.Mediator.UnitTests` |
| `Send_Command_WithObservingErrorHandler_ShouldRethrowByDefault` | `LiteBus.Mediator.UnitTests` |
| `Send_CommandWithResult_WhenErrorHandlerSetsHandledResult_ShouldReturnFallbackResult` | `LiteBus.Mediator.UnitTests` |
| `Completion_runs_with_Succeeded_when_the_handler_succeeds` | `LiteBus.Mediator.UnitTests` |
| `Completion_runs_with_Failed_when_the_handler_throws` | `LiteBus.Mediator.UnitTests` |
| `Completion_runs_with_Denied_and_carries_the_reason` | `LiteBus.Mediator.UnitTests` |
| `Completion_receives_the_result_typed_when_the_handler_asks_for_it` | `LiteBus.Mediator.UnitTests` |
| `A_suppressed_completion_fault_is_attached_to_the_original_exception` | `LiteBus.Mediator.UnitTests` |
| `Completion_runs_with_Canceled_and_the_cancellation_still_propagates` | `LiteBus.Mediator.UnitTests` |
| `Direct_completion_handlers_run_before_indirect_ones` | `LiteBus.Mediator.UnitTests` |
| `A_failing_completion_handler_does_not_replace_the_original_fault` | `LiteBus.Mediator.UnitTests` |
| `An_answering_shortcut_skips_the_main_handler_and_reports_Answered` | `LiteBus.Mediator.UnitTests` |
| `The_stage_order_is_guard_then_validator_then_shortcut_then_pre_handler` | `LiteBus.Mediator.UnitTests` |
| `Every_validator_runs_so_the_caller_sees_all_failures_at_once` | `LiteBus.Mediator.UnitTests` |
| `A_lowest_priority_shortcut_cannot_answer_ahead_of_a_guard` | `LiteBus.Mediator.UnitTests` |
| `A_denial_reports_Denied_and_reaches_the_caller_as_an_exception` | `LiteBus.Mediator.UnitTests` |
| `A_denial_is_a_decision_so_error_handlers_do_not_see_it` | `LiteBus.Mediator.UnitTests` |
| `A_denial_may_hand_the_caller_a_refusal_value_instead_of_throwing` | `LiteBus.Mediator.UnitTests` |
| `Suppressing_post_handlers_still_reports_Succeeded` | `LiteBus.Mediator.UnitTests` |
| `A_short_circuit_supplies_the_result_the_caller_receives` | `LiteBus.Mediator.UnitTests` |
| `A_short_circuit_without_a_required_result_is_a_configuration_error` | `LiteBus.Mediator.UnitTests` |
| `Pre_handlers_after_a_stopping_directive_do_not_run` | `LiteBus.Mediator.UnitTests` |
| `An_event_shortcut_can_skip_the_reactions_to_an_already_handled_event` | `LiteBus.Mediator.UnitTests` |
| `An_event_completion_handler_observes_a_successful_broadcast` | `LiteBus.Mediator.UnitTests` |
| `Each_message_type_reaches_its_own_contract_on_a_shared_handler` | `LiteBus.Mediator.UnitTests` |
| `A_result_returning_message_reaches_the_typed_post_handler_contract` | `LiteBus.Mediator.UnitTests` |

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
