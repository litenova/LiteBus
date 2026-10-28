# Fault Refusal Classification

- **ID**: `mediator.fault-refusal-classification`
- **Name**: Fault refusal classification
- **Maturity**: GA
- **Summary**: Recognizes a fault a guard never saw and ends the mediation as a named refusal instead of an exception.
- **Guide**: [The handler pipeline](../../concepts/handler-pipeline.md)

## What It Does

A fault raised after the decision stages never passed a guard: a duplicate key reported when a unit of work commits, or a concurrency fault from the main handler, reaches the caller as an exception that every application maps by hand at its boundary. A fault refusal classifier moves that mapping into the pipeline by recognizing the fault and naming the refusal the mediation ends as instead.

A recognized fault ends the mediation as the classifier's refusal: `TrySendAsync` and `TryQueryAsync` report it as a value, `SendAsync` raises `LiteBusMessageDeniedException` or `LiteBusMessageInvalidException` per the refusal's outcome, refusal mappers see it like any other refusal, and the audit outcome records the refusal rather than a failure. The fault stays available as the inner exception of the refusal exception, so the cause is never lost to a caller that catches it.

## Public Surface

```csharp
public sealed class DuplicateKeyClassifier : IFaultRefusalClassifier
{
    public bool TryClassify(Exception exception, out Refusal refusal)
    {
        if (exception is DuplicateKeyException duplicate)
        {
            refusal = Refusal.Denied($"'{duplicate.Reference}' is already taken", "REFERENCE_TAKEN");
            return true;
        }

        refusal = default;
        return false;
    }
}

registry.AddMessaging(messaging =>
{
    messaging.UseFaultRefusalClassifier<DuplicateKeyClassifier>();
});
```

For PostgreSQL, `PostgreSqlConstraintRefusalClassifier` recognizes unique violations (`23505`) and exclusion violations (`23P01`) on a configured constraint map, found anywhere in the inner exception chain:

```csharp
registry.AddMessaging(messaging =>
{
    messaging.UseFaultRefusalClassifierInstance(new PostgreSqlConstraintRefusalClassifier(
        new Dictionary<string, string> { ["ux_orders_reference"] = "ORDER_REFERENCE_TAKEN" }));
});
```

| API | Role |
| --- | --- |
| `IFaultRefusalClassifier.TryClassify(Exception, out Refusal)` | Recognizes a fault and names the refusal the mediation ends as |
| `MessageModuleBuilder.UseFaultRefusalClassifier<TClassifier>()` | Registers a classifier by type, as a singleton |
| `MessageModuleBuilder.UseFaultRefusalClassifierInstance(classifier)` | Registers a classifier instance |
| `PostgreSqlConstraintRefusalClassifier` | Shipped classifier over PostgreSQL unique and exclusion violations, from `LiteBus.Storage.PostgreSql` |
| `MediationExceptionFilters.TryClassifyRefusal(classifiers, exception, out refusal)` | Consults the registered classifiers in order with first match winning |

## Packages

- `LiteBus.Messaging.Abstractions` (`IFaultRefusalClassifier`, `MediationExceptionFilters`)
- `LiteBus.Messaging` (`UseFaultRefusalClassifier`, strategy integration)
- `LiteBus.Storage.PostgreSql` (`PostgreSqlConstraintRefusalClassifier`)

## Requires

- `mediator.handler-pipeline`
- `mediator.auditing` (a classified fault is recorded as a refusal, not a failure)

## Invariants

- Classifiers are consulted in registration order and the first match wins, so the most specific classifier registers first.
- Classifiers are singletons and must be stateless: a pure predicate over the exception, with no I/O and no side effects.
- A fault from any stage is offered, including the completion stage, where a commit fault becomes a refusal the caller branches on.
- Cancellation is never classified, and an exception that already carries a refusal is never reclassified.
- A classified fault skips the error handlers: it is a refusal, not a fault. Completion handlers still run, observing the refusal outcome.
- A classified completion fault supersedes the ending the mediation had reached: handlers that already ran keep the outcome they observed, the remaining handlers do not run, and the caller receives the refusal exception. Refusal mappers do not apply there, because the main handler already produced the caller's value, and mediation telemetry records the outcome observed when the completion stage began.
- `EvaluateAsync` reports a classified decision-stage fault as the denial or invalid decision the message would end as when sent.
- With no classifier registered, every fault behaves exactly as before.

## Non-Goals

- Retrying the fault; classification decides the ending, not a second attempt.
- Replacing guards; anything decidable before the work runs belongs to a guard, which runs first.
- Reclassifying a refusal into a different refusal.

## Observability

No classifier-specific meter or activity source. A classified fault is recorded as a refusal in mediation telemetry and in the audit trail, not as a failure.

Operational alternatives:
- Log inside the classifier when a new fault shape needs a mapping.
- Switch the mediation decision counter on the refusal code the classifier supplies.

## Test Coverage

### Covered

| Test method | Project |
| --- | --- |
| `Classified_main_fault_returns_a_denial_from_TrySendAsync` | `LiteBus.Mediator.UnitTests` |
| `Classified_main_fault_throws_the_refusal_from_SendAsync` | `LiteBus.Mediator.UnitTests` |
| `Classified_main_fault_never_reaches_error_handlers` | `LiteBus.Mediator.UnitTests` |
| `Classified_main_fault_runs_completion_with_the_refusal_outcome` | `LiteBus.Mediator.UnitTests` |
| `Classified_fault_reaches_a_registered_refusal_mapper` | `LiteBus.Mediator.UnitTests` |
| `Invalid_classification_returns_Invalid_and_throws_the_invalid_exception` | `LiteBus.Mediator.UnitTests` |
| `Commit_fault_in_completion_becomes_a_refusal` | `LiteBus.Mediator.UnitTests` |
| `Without_a_classifier_a_commit_fault_propagates_unchanged` | `LiteBus.Mediator.UnitTests` |
| `Without_a_classifier_a_main_fault_reaches_error_handlers` | `LiteBus.Mediator.UnitTests` |
| `Cancellation_is_never_classified` | `LiteBus.Mediator.UnitTests` |
| `First_registered_classifier_wins` | `LiteBus.Mediator.UnitTests` |
| `Unrecognized_faults_keep_the_failure_path` | `LiteBus.Mediator.UnitTests` |
| `Guard_fault_is_classified` | `LiteBus.Mediator.UnitTests` |
| `Post_handler_fault_is_classified` | `LiteBus.Mediator.UnitTests` |
| `Classified_query_fault_returns_a_refusal_from_TryQueryAsync` | `LiteBus.Mediator.UnitTests` |
| `Classified_event_fault_throws_the_refusal_from_PublishAsync` | `LiteBus.Mediator.UnitTests` |
| `Evaluate_reports_a_classified_guard_fault_as_a_denial` | `LiteBus.Mediator.UnitTests` |
| `Evaluate_without_a_classifier_propagates_the_guard_fault` | `LiteBus.Mediator.UnitTests` |
| `A_classified_fault_is_audited_as_a_denial_not_a_failure` | `LiteBus.Mediator.UnitTests` |

### Untested

- A shipped PostgreSQL classifier mapping over a live constraint violation end to end.
- Two classifiers where the second matches a fault the first one partially recognizes.

### Out-of-Scope

- Durable processor retry and dead-letter behavior for classified refusals.
- Database constraint design and migration management.

## Deep Docs

- [The handler pipeline](../../concepts/handler-pipeline.md)
- [Validation](../../concepts/validation.md)
