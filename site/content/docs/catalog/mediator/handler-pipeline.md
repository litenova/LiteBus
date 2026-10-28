# Handler Pipeline

- **ID**: `mediator.handler-pipeline`
- **Name**: Handler pipeline
- **Maturity**: GA
- **Summary**: Defines pre, main, post, error, and completion stage execution for command, query, and event mediation. The pre stage holds four framework-ordered roles: guard, validator, shortcut, and pre-handler.
- **Guide**: [The handler pipeline](../../concepts/handler-pipeline.md)

## What It Does

The pipeline is shared across semantic mediators:
1. Pre-handlers.
2. Main handler(s).
3. Post-handlers.
4. Error-handlers on recoverable failures.
5. Completion handlers on every outcome path.

Direct and indirect handlers run in a fixed order:
- Guard, validator, shortcut, and pre: indirect then direct.
- Post: direct then indirect.
- Error: indirect then direct.
- Completion: one pass over the merged direct and indirect sets, ordered by `[HandlerPriority]` then registration sequence.

The pre stage is four framework-ordered stages, not one. Guards run first and refuse through `Verdict`. Validators run next and report failures through `Validity`. Shortcuts run third and answer through `Shortcut` or `Shortcut<TResult>`, or deny through `Shortcut.Deny` when the refusal can only be decided at the shortcut stage. Plain pre-handlers run last and cannot stop the pipeline by returning.

Priority orders handlers inside a stage and never reorders the stages, so a globally registered shortcut cannot answer ahead of a message-specific guard, and a malformed message cannot claim an idempotency key. Guards and shortcuts stop at the first decision, while the validator stage runs every validator and collects their failures.

A refusal with no registered `IMessageRefusalMapper<TMessage, TMessageResult>` raises `LiteBusMessageDeniedException` or `LiteBusMessageInvalidException`. Both are excluded from the recoverable filter, so error handlers never see a decision as a fault. `LiteBusMessageDeniedException` carries `DeniedBy` and `LiteBusMessageInvalidException` carries `RejectedBy`, and each names the handler that decided it.

A shortcut denial ends the mediation exactly as a guard denial does. Completion handlers observe `Denied`, error handlers do not run, and a registered refusal mapper applies.

Any stage may call `IExecutionContext.SuppressPostHandlers()` to skip the remaining post-handlers without changing the outcome.

A fault raised after the decision stages can still end as a refusal. `IFaultRefusalClassifier` entries registered with `MessageModuleBuilder.UseFaultRefusalClassifier` recognize the fault and name the refusal the mediation ends as. See [Fault refusal classification](fault-refusal-classification.md).

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
| `IMessageGuard<TMessage>` | Guard stage contract. Refuses the message |
| `IMessageValidator<TMessage>` | Validator stage contract. Reports the message malformed |
| `IMessageRefusalMapper<TMessage, TResult>` | Turns a refusal into the value the caller receives |
| `IMessageShortcut<TMessage>` / `IMessageShortcut<TMessage, TResult>` | Shortcut stage contracts that may answer without the handler |
| `Verdict` | Allow or deny, with a reason and an optional code |
| `Validity` / `ValidationFailure` | Valid, or the failures a validator reported |
| `ThrowingValidator<TMessage, TException>` | Migration adapter over a validator body that reports failure by throwing. Axis forms are `ThrowingCommandValidator` and `ThrowingQueryValidator` |
| `Refusal` | The outcome, reason, and code handed to a refusal mapper. Only `Refusal.Denied` or `Refusal.Invalid` |
| `Shortcut` / `Shortcut<TResult>` | `None` to carry on, `Answer` with a reason, a code, and a result when the message produces one, or `Deny` with a reason and a code |
| `IFaultRefusalClassifier` / `MessageModuleBuilder.UseFaultRefusalClassifier` | Turns a fault raised after the decision stages into the refusal the mediation ends as |
| `MediationExceptionFilters.IsRefusal` / `IsRetryableDispatchException` / `IsCallerCancellation` | Distinguish a decision from a fault, recognize a fault worth retrying, and recognize the caller cancelling inside a wrapper |
| `PreStage` | `Guard`, `Validator`, `Shortcut`, `PreHandler` in the framework-fixed stage order. Covers the pre stage only |
| `PipelineDecision` | What a pre-stage handler reported: `Continue`, or a stop carrying the outcome, reason, result, and `DecidedBy` |
| `LiteBusMessageDeniedException` | Raised when a refusal supplies no result for the caller. Carries `DeniedBy` naming the guard |
| `LiteBusMessageInvalidException` | Raised for a validation failure. Carries `RejectedBy` naming the validator |
| `IAsyncMessageHandler<TMessage>` / `IAsyncMessageHandler<TMessage, TResult>` | Main handler contracts |
| `IMessagePostHandler<TMessage, TResult>` | Post stage contract |
| `IStreamQueryPostHandler<TQuery, TQueryResult>` | Post stage contract for stream queries |
| `IQueryRefusalMapper<TQuery, TQueryResult>` | Refusal mapper for single-result queries |
| `IStreamQueryRefusalMapper<TQuery, TQueryResult>` | Refusal mapper for stream queries, mapping to `IAsyncEnumerable<T>` semantics. Events have no mapper, so a refusal there always raises |
| `MessageModuleBuilder.UseTelemetry(MediationTelemetryOptions)` | Selects which mediation telemetry is recorded |
| `IExecutionContext.SuppressPostHandlers()` | Skips the post-handlers that have not run yet |
| `IMessageErrorHandler<TMessage, TResult>` | Error stage contract |
| `MessageErrorContext<TMessage, TResult>` | Typed error data and shared recovery outcome |
| `IMessageCompletionHandler` / `IMessageCompletionHandler<TMessage>` / `IMessageCompletionHandler<TMessage, TResult>` | Completion stage contracts |
| `MessageCompletionContext` and its typed views | Read-only outcome, result, exception, reason, code, duration |
| `MediationResult` / `MediationResult<TResult>` | What `TrySendAsync` and `TryQueryAsync` return: the outcome, the value, the reason, the code, and the validation failures |
| `MediationDecision` | What `EvaluateAsync` returns: what the guard and validator stages would say, with nothing performed |
| `MessageContextExtensions.RunAsyncDecisionStages` | Runs guards then validators and stops, for a custom strategy backing an evaluation |
| `IMessageReader.Explain(Type)` / `MessagePipelinePlan` | Everything that will run for one message, in the order it will run |
| `MediationOutcome` | `Succeeded`, `Answered`, `Denied`, `Invalid`, `Failed`, `Canceled` |
| `LiteBusMediationTelemetry` | Public instrument and attribute names for the `LiteBus.Mediation` source and meter |
| `MediationExceptionData.SuppressedCompletionFaults` | Key under which a suppressed completion fault is attached to the original exception |
| `HandlerPriorities` | Reserved priority band for handlers shipped by LiteBus |
| `SingleAsyncHandlerMediationStrategy<TMessage, TResult>` | Single main handler orchestration |
| `SingleStreamHandlerMediationStrategy<TMessage, TResult>` | Stream query orchestration |
| `AsyncBroadcastMediationStrategy<TMessage>` | Event broadcast orchestration |
| `MessageContextExtensions.RunAsyncPreStages/RunAsyncPostHandlers/RunAsyncErrorHandlers/RunAsyncCompletionHandlers` | Stage execution helpers in the `LiteBus.Messaging` assembly |
| `MessageContextExtensions.ResolveRefusalResult<TMessageResult>` | Applies the registered refusal mapper, or raises when none covers the message |
| `PipelineDispatch` | Delegate bound at registration to the closed contract a handler was discovered from, plus `StageFor` |
| `PipelineContracts` | The single table declaring every dispatchable contract with its family, invoker, and stage |
| `IPreStageHandlerDescriptor.Stage` | The stage that runs a discovered pre-stage handler |
| `IMessageDependencies.HasPreStageHandlers` | Whether a stage holds any handler, so an empty one costs nothing |
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

- Single-handler strategies resolve one main handler at the winning level. Direct handlers come first, and an indirect (base-type) handler applies only when no direct handler survives filtering.
- Post-handler result override uses `executionContext.MessageResult`.
- Recoverable exceptions route to error handlers with the caller's explicit cancellation token.
- A fault the recoverable filter excludes bypasses the error stage but is still recorded before completion runs. Completion observes `Failed` with the exception, or `Canceled` for a cancellation, and never `Succeeded` for a mediation that threw.
- Error handlers suppress the original exception only by setting their shared context outcome to `Handled`.
- A fault an error handler recovered completes as `Failed` with no recorded exception, which distinguishes it from a fault that propagated. A completion-handler fault on that path propagates instead of being suppressed.
- On a result-bearing message, an error handler that sets `Handled` without a usable `HandledResult` recovers the message and the caller receives the default value (`null` for a reference type). A global error handler cannot know the result type, so this is how it absorbs a fault. A handler with a fallback value sets `HandledResult`.
- Event broadcast may execute handlers concurrently based on event execution settings.
- Completion handlers run exactly once per mediation, on every outcome path.
- Completion handlers cannot change the outcome. The context is read-only.
- `EvaluateAsync` runs the guard and validator stages only. A shortcut and a pre-handler act rather than decide, so an evaluation cannot claim an idempotency key or do a pre-handler's work.
- `TrySendAsync` and `TryQueryAsync` return a refusal as a value and still throw a genuine fault, which is the line the pipeline already draws between a decision and a fault.
- `Reason` and `Code` mean the same thing on `Verdict`, `Shortcut`, `PipelineDecision`, `MessageCompletionContext`, `MediationResult`, and `MediationDecision`: prose for a person, and a value a later stage can switch on.
- Completion handlers receive `CancellationToken.None`, and the stage runs to the end on every path.
- When a completion handler throws while an exception is ending the mediation, its fault is attached to that exception under `MediationExceptionData.SuppressedCompletionFaults`. Otherwise it propagates.
- Stream completion fires on enumerator disposal, so an unenumerated stream produces no completion record.
- A refusal mapper's stream is enumerated through the same runner as the handler's stream, so a fault in it is offered to the error stage and the ambient scope wraps every advance.
- Only a guard, a validator, or a shortcut can stop the pipeline. `Answered`, `Denied`, and `Invalid` all mean the main handler never ran.
- A denial is not routed to error handlers and is not reported as `Faulted`.
- Suppressing post-handlers reports `MediationOutcome.Succeeded`, because the main handler ran.
- A shortcut that answers a result-returning message must supply a result of the expected type, or mediation throws `LiteBusConfigurationException`. Analyzer rule `LB1019` reports a shortcut that used the untyped contract for such a message. A guard has no such duty, so the untyped guard is correct everywhere. A denial carries no result and is resolved through a mapper, so denying a result message is safe through either shortcut contract. Direct registration of the untyped contract on a result message is still rejected.
- A shortcut denial reports `Denied` exactly like a guard denial. `TrySendAsync` reports it as a value, `SendAsync` raises `LiteBusMessageDeniedException` naming the shortcut in `DeniedBy`, completion handlers observe it, error handlers skip it, and a registered refusal mapper applies. Anything a guard could decide belongs to a guard, which runs first.
- One class may implement pipeline contracts for several message types. Each dispatch reaches the contract recorded in its descriptor.

## Non-Goals

- Automatic retries or delayed re-execution on failures.
- Transaction boundaries across multiple handlers.
- Pipeline-level persistence of stage artifacts.

## Observability

Mediation telemetry is recorded by default and exported through `LiteBus.Messaging.Extensions.OpenTelemetry` (`AddLiteBusMediationInstrumentation`, `AddLiteBusMediationMetrics`). Names live on `LiteBusMediationTelemetry` (`LiteBus.Mediation` source and meter) and form a stable consumer contract:

| Instrument | Default | Records |
| --- | --- | --- |
| `litebus.mediation.duration` | On | Total mediation duration histogram |
| `litebus.mediation.count` | On | Count tagged by outcome (`litebus.message`, `litebus.outcome`, plus `litebus.code` when a decision stopped the mediation) |
| `litebus.mediation.decisions` | On | Decision counter tagged by stage (`litebus.stage`) and handler (`litebus.decided_by`) |
| `litebus.mediation.stage.duration` | Off | Per-stage duration histogram (`StageMetrics`) |

One span per mediation is on by default, and per-stage spans are opt-in (`StageSpans`). Select both through `MessageModuleBuilder.UseTelemetry(new MediationTelemetryOptions { StageSpans = true, StageMetrics = true })`. Only a `Failed` outcome sets the error status. Denials and validation refusals keep a success status, so error views show faults only.

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
| `Priority_orders_completion_handlers_across_the_direct_and_indirect_split` | `LiteBus.Mediator.UnitTests` |
| `A_failing_completion_handler_does_not_replace_the_original_fault` | `LiteBus.Mediator.UnitTests` |
| `An_answering_shortcut_skips_the_main_handler_and_reports_Answered` | `LiteBus.Mediator.UnitTests` |
| `The_stage_order_is_guard_then_validator_then_shortcut_then_pre_handler` | `LiteBus.Mediator.UnitTests` |
| `Every_validator_runs_so_the_caller_sees_all_failures_at_once` | `LiteBus.Mediator.UnitTests` |
| `A_lowest_priority_shortcut_cannot_answer_ahead_of_a_guard` | `LiteBus.Mediator.UnitTests` |
| `A_denial_reports_Denied_and_reaches_the_caller_as_an_exception` | `LiteBus.Mediator.UnitTests` |
| `A_denial_is_a_decision_so_error_handlers_do_not_see_it` | `LiteBus.Mediator.UnitTests` |
| `A_registered_mapper_hands_the_caller_a_refusal_value_instead_of_throwing` | `LiteBus.Mediator.UnitTests` |
| `Suppressing_post_handlers_still_reports_Succeeded` | `LiteBus.Mediator.UnitTests` |
| `An_answering_shortcut_supplies_the_result_the_caller_receives` | `LiteBus.Mediator.UnitTests` |
| `An_untyped_shortcut_on_a_message_with_a_result_is_rejected_at_registration` | `LiteBus.Mediator.UnitTests` |
| `An_untyped_shortcut_is_rejected_whichever_side_registers_first` | `LiteBus.Mediator.UnitTests` |
| `A_global_untyped_shortcut_reaching_a_message_with_a_result_reports_the_shortcut_by_name` | `LiteBus.Mediator.UnitTests` |
| `Pre_handlers_after_a_stopping_decision_do_not_run` | `LiteBus.Mediator.UnitTests` |
| `A_denying_shortcut_reports_Denied_through_TrySendAsync` | `LiteBus.Mediator.UnitTests` |
| `A_denying_shortcut_throws_through_SendAsync_naming_the_shortcut` | `LiteBus.Mediator.UnitTests` |
| `A_shortcut_denial_reaches_completion_handlers_as_Denied` | `LiteBus.Mediator.UnitTests` |
| `A_shortcut_denial_is_a_decision_so_error_handlers_do_not_see_it` | `LiteBus.Mediator.UnitTests` |
| `A_refusal_mapper_applies_to_a_shortcut_denial` | `LiteBus.Mediator.UnitTests` |
| `A_mapped_shortcut_denial_reports_Denied_with_a_value_through_TrySendAsync` | `LiteBus.Mediator.UnitTests` |
| `A_denying_typed_shortcut_throws_when_no_mapper_covers_the_command` | `LiteBus.Mediator.UnitTests` |
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

## References

- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Execution context](../../concepts/execution-context.md)
- [Message definitions](../../concepts/message-definitions.md)
- [Auditing](../../concepts/auditing.md)
