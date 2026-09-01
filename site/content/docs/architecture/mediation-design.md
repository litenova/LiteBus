# Mediation Layer Design Rules

This page states the rules the mediation layer follows, so that a new contract can be derived rather than guessed at from whichever neighbour it happens to resemble. [API Design Reference](api-design.md) covers the durable and writer surfaces and defers mediation to this page.

Every rule here is one the current code follows. Where the code deviates, the deviation is listed in [Known Deviations](#known-deviations) rather than quietly omitted.

## Who This Is For

- Contributors adding a pipeline stage, a role, or an axis
- Reviewers deciding whether a proposed name is consistent or merely plausible
- Anyone who has asked why a type is called what it is called

**Prerequisites:** [The Handler Pipeline](../concepts/handler-pipeline.md) for behavior, [Pipeline Vocabulary](../reference/glossary.md#pipeline-vocabulary) for the word list.

## The Stage Model

Mediation has five stages. The pre stage holds four roles; the rest hold one each.

| Stage | Roles | May end the pipeline | Runs when |
| --- | --- | --- | --- |
| Pre | guard, validator, shortcut, pre-handler | guard, validator, and shortcut only | always |
| Main | main handler | by throwing | nothing stopped the pipeline |
| Post | post-handler | by throwing | the main handler returned |
| Error | error handler | by rethrowing | a recoverable exception escaped |
| Completion | completion handler | never | always, exactly once |

**Rule 1. Ending the pipeline is a capability, and only the three decision roles have it.** A pre-handler, a post-handler, and a completion handler cannot stop the work by returning. This is why the pre stage is four contracts rather than one: the framework has to be able to tell, before invoking a handler, whether it is allowed to end the mediation.

**Rule 2. The stage order is fixed by construction, not by convention.** `PreStage` declares the order, `PipelineContracts.StagesInOrder` reads it from the enum, and priority orders handlers only within a stage. A guard therefore always runs before a shortcut, whatever priority either carries and whichever is registered globally.

## Contract Rules

**Rule 3. Every stage has a memberless marker plus one or more generic role contracts.** The marker exists for discovery; the generic contract carries the method. The marker is memberless on purpose: a class implementing a stage contract for two message types would have no most-specific implementation of a default interface method and would not compile.

```csharp
public interface IMessagePreStageHandler;                                  // marker, for discovery
public interface IMessageGuard<in TMessage> : IMessagePreStageHandler      // role, carries the method
```

**Rule 4. The pipeline invokes a handler through the closed contract its descriptor recorded at registration**, never by searching the type at dispatch. That is what lets one class serve several message types and several roles at once.

**Rule 5. An axis contract is an alias, never a redeclaration.** `ICommandGuard<TCommand>` adds a constraint and nothing else. It never restates a method, and it never introduces a member the messaging contract lacks.

```csharp
public interface ICommandGuard<in TCommand> : IMessageGuard<TCommand>
    where TCommand : ICommand;
```

**Rule 6. Arity follows the message, not the stage.** A stage offers only the shapes its axis can produce.

| Axis | Message shapes | Shortcut contracts offered |
| --- | --- | --- |
| Command | `ICommand` and `ICommand<TResult>` | untyped and typed |
| Query | `IQuery<TResult>` | typed, plus the stream form |
| Event | `IEvent`, never a result | untyped only |

**Rule 7. A placeholder result type is allowed only where the stage reads the result.** A post-handler binds `object` because it only observes the value. A shortcut must not, because it supplies the value, and binding `object` would force an author to invent one for a message that has none. Where the stage supplies a result, use separate arity.

## Vocabulary Rules

**Rule 8. The noun is shared; the verb names the act.** `Result` is the one noun for the value a message produces. Each stage that produces one gets its own verb, because the acts genuinely differ.

| Contract | Verb | Act |
| --- | --- | --- |
| `IMessageHandler` | `HandleAsync` | does the work |
| `IMessageShortcut` | `TryAnswerAsync` | supplies a result the work would have produced |
| `IMessageRefusalMapper` | `Map` | turns a refusal into the result shape |

Do not invent a second noun for the value, and do not force one verb across different acts.

**Rule 9. Factory verb, flag participle, and outcome participle are the same word.** This grid holds across all three decision types and is the reason `Answer` is not interchangeable with `Respond`, `SetResult`, or `FromResult`.

| Role | Stop factory | Flag | `MediationOutcome` | `AuditOutcome` |
| --- | --- | --- | --- | --- |
| Guard | `Verdict.Deny` | `IsDenied` | `Denied` | `Denied` |
| Validator | `Validity.Invalid` | `IsInvalid` | `Invalid` | `Invalid` |
| Shortcut | `Shortcut.Answer` | `IsAnswered` | `Answered` | `Succeeded` |

**Rule 10. A word names one thing.** The full list is [Pipeline Vocabulary](../reference/glossary.md#pipeline-vocabulary). Two entries carry most of the weight: `Refusal` is the category holding `Denied` and `Invalid` and is never a synonym for either; `Answered` is never called short-circuiting, skipping, or cancelling.

**Rule 11. Use a state noun where English has one, and a mechanism noun only where it does not.** A guard returns a `Verdict` and a validator returns a `Validity` because permission and correctness both have state nouns. Already-done-ness has none, so the shortcut returns a `Shortcut`. Reach for a mechanism noun last, and say in the type's own documentation why no state noun was available.

## Decision Type Rules

**Rule 12. A decision is a return value.** Not an exception, not ambient state. This is what makes the compiler require it, keeps an expected control-flow path off the exception path, and stops a handler skipping the work by accident. `IExecutionContext.Abort` was removed in v7 for breaking all three.

**Rule 13. The continue value is `default`.** `Verdict.Allow`, `Validity.Valid`, `Shortcut.None`, and `PipelineDecision.Continue` are all the default value of a readonly struct, so the common path allocates nothing.

**Rule 14. A decision type can express only what its role may decide.** A `Verdict` cannot carry a result, so a guard cannot answer. A `Validity` cannot deny. A `Refusal` can only be `Denied` or `Invalid`, which is why it has two factories and no public constructor. Prefer making the wrong decision unrepresentable over validating it later.

**Rule 15. A decision reaching the caller as a value is the application's choice, not the framework's.** Without an `IMessageRefusalMapper`, a refusal raises. With one, it becomes a result. The mapping lives in one place per message rather than in each guard.

## Registration and Dispatch Rules

**Rule 16. Every dispatchable contract is one row in `PipelineContracts`.** The row names the contract, its family, its invoker, and, for a pre-stage contract, its stage and aggregation policy. The dispatch factory, all four descriptor builders, and the stage runner read from that table. Nothing reads a hardcoded contract list.

**Rule 17. Registration rejects what it can prove wrong.** A configuration error that is detectable when the registry links a handler to a message must be raised there, not left to the first dispatch that happens to take the branch. Registration currently rejects a pipeline marker that names no message type, an unsupported open generic handler shape, two definitions declaring the same metadata key, and an untyped shortcut on a message whose main handler produces a result.

**Rule 18. Put the check at the earliest point that can prove the fault, and say where the later ones still apply.** The same mistake is often catchable at several points with different confidence.

| Point | Catches | Trade-off |
| --- | --- | --- |
| Compiler | Anything a constraint can express | Cannot express "`ICommand` but not `ICommand<T>`" |
| Analyzer | Declarations, at build time | A warning, suppressible, absent without the package |
| Registration | Anything provable from the linked descriptors | Cannot judge a handler registered against a base type |
| Dispatch | The remainder | Only fires when the branch is taken, possibly long after deployment |
| Diagnostic check | Environmental gaps, such as a missing `IAuditTrail` | Reports, does not prevent |

The untyped shortcut is the worked example: the compiler cannot express the constraint, so LB1019 reports the declaration, registration rejects a direct registration outright, and dispatch names the offending shortcut for the indirect case neither of the earlier points can prove.

**Rule 19. An error message names the offender and the fix.** Not just the symptom. The registration and dispatch errors for the untyped shortcut both name the shortcut type, the message type, the result type, and the contract to implement instead.

**Rule 20. Handlers LiteBus itself ships sit at or above `HandlerPriorities.ReservedFloor`.** An application handler with no explicit priority runs before all of them. Ordering against a LiteBus handler is a documented guarantee, not something each application rediscovers.

## Adding a New Pre-Stage Role

1. Add a member to `PreStage`, positioned where it must run. The enum order is the execution order.
2. Add the role contract deriving from `IMessagePreStageHandler`, and its decision type if it needs one.
3. Add the static invoker to `PipelineDispatch`, `internal static` so `nameof` reaches it from the table.
4. Add one row to `PipelineContracts` naming the contract, `PipelineFamily.PreStage`, the invoker, the stage, and the aggregation policy.
5. Add the alias contract on each axis whose messages can carry the role. Apply Rule 6 before adding it to all three.
6. Add a member to `MediationOutcome` if the role can end the mediation in a new way, and map it in `DefaultAuditOutcomeMapper`.
7. Extend `PipelineContractTableTests`, which asserts the table's invariants rather than any one row.

Steps 3 and 4 are the whole registration and dispatch wiring. If a change needs edits anywhere else, the table is missing a column.

## Adding a New Axis

1. Define the message contracts, and decide for each stage which shapes the axis can carry (Rule 6).
2. Alias only the stages that apply, with no new members (Rule 5).
3. List the accepted contracts in the module builder's discovery set.
4. State in the axis documentation which stages it does not offer and why, as the [event axis does for auditing](../concepts/auditing.md#events-are-not-audited).

## Known Deviations

Recorded so they are decisions rather than precedents.

| Deviation | Where | Why it stands |
| --- | --- | --- |
| Post-handler binds `object` for result-less messages instead of using arity | `ICommandPostHandler<TCommand>`, `IQueryPostHandler<TQuery>`, `IEventPostHandler<TEvent>` | Permitted by Rule 7, because a post-handler reads the result rather than supplying it. It is still the only stage that does this, so do not treat it as the default |
| The role and its return type share a name | `IMessageShortcut` returns `Shortcut` | Rule 11. No state noun exists for already-done-ness |
| The guard's method does not echo its role | `IMessageGuard.DecideAsync` | `GuardAsync` says nothing, and `CheckAsync` already means a health probe on `IDiagnosticCheck` |
| `MediationOutcome.Canceled` uses one `l` | `MediationOutcome` | Matches `OperationCanceledException`, which the pipeline catches to produce it |

## Next

- [The Handler Pipeline](../concepts/handler-pipeline.md): what each stage does at runtime
- [Pipeline Vocabulary](../reference/glossary.md#pipeline-vocabulary): the word list these rules enforce
- [API Design Reference](api-design.md): the durable and writer surfaces
- [Architecture Decisions](decisions.md): choices that predate these rules
