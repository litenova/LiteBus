# Glossary

LiteBus documentation uses a fixed vocabulary. Each pipeline word names exactly one thing. This page defines those terms, and each entry links to the page that explains it in depth.

> **This page is for** anyone who meets an unfamiliar LiteBus term and wants the meaning the rest of the pages use.
>
> **After reading it** you can name each concept the way the documentation names it. To see the pipeline words in context, go on to [The Handler Pipeline](../concepts/handler-pipeline.md).

## Core Concepts

| Term | Definition |
| --- | --- |
| Mediator | The command, query, and event mediation layer that routes a message to its handlers. Handlers execute in the caller's process. LiteBus exposes typed mediators (`ICommandMediator`, `IQueryMediator`, `IEventMediator`) over a shared `IMessageMediator`. See [Architecture](../architecture/README.md). |
| Command | A message expressing an intention to change state. It resolves to exactly one main handler. See [Command Module](../concepts/commands.md). |
| Query | A message that reads state and returns a result. It resolves to exactly one main handler. See [Query Module](../concepts/queries.md). |
| Event | A message reporting a fact. It broadcasts to every matching handler, or none. See [Event Module](../concepts/events.md). |
| Handler | A class that processes a message. The single class that does the work is the main handler. |

## Pipeline

| Term | Definition |
| --- | --- |
| Pipeline | The pre stage, main handler, post-handlers, error-handlers, and completion handlers for one message. See [The Handler Pipeline](../concepts/handler-pipeline.md). |
| Pre stage | The whole phase before the main handler. It holds four roles: guard, validator, shortcut, and pre-handler. The `PreStage` enum names those roles. |
| Guard | A guard decides whether the message is permitted to proceed. It returns `Verdict`. |
| Validator | A validator decides whether the message is well-formed. It returns `Validity`. |
| Shortcut | A shortcut decides whether the answer is already known. It returns `Shortcut` or `Shortcut<TResult>`. |
| Pre-handler | The fourth pre-stage role, and the one LiteBus does not name after a job. It prepares a message that is going to be handled, and it cannot end the pipeline by returning. |
| Main handler | The class that does the work. There is one for a command or query, and zero to many for an event. |
| Post-handler | A post-handler runs after the main handler succeeds. |
| Error-handler | An error-handler runs when a stage throws a recoverable exception. |
| Completion handler | A completion handler runs once at the end of every mediation, on every path, and observes how it ended. |
| Refusal mapper | A refusal mapper is not a stage. It turns a denial or a validation failure into the result the caller receives, in place of raising. |
| Execution context | Ambient per-mediation state (`AsyncLocal`). See [Execution Context](../concepts/execution-context.md). |

## Pipeline Vocabulary

These words each name exactly one thing, and nothing else in LiteBus is called by them. Where the documentation, an XML comment, and a type name could disagree, this table decides. The rules behind each word, and how to name the next one, are in [Mediation Layer Design Rules](../architecture/mediation-design.md).

| Word | Means | Do not write |
| --- | --- | --- |
| **Denied** | A guard refused the message. `Verdict.Deny`, `MediationOutcome.Denied`, `AuditOutcome.Denied`, `LiteBusMessageDeniedException`. | rejected, refused (on its own) |
| **Invalid** | A validator found the message malformed. `Validity.Invalid`, `MediationOutcome.Invalid`, `AuditOutcome.Invalid`, `LiteBusMessageInvalidException`. | denied, rejected, failed |
| **Answered** | A shortcut supplied the result, so the main handler never ran. `Shortcut.Answer`, `MediationOutcome.Answered`, recorded as `AuditOutcome.Succeeded`. | short-circuited, skipped, cancelled |
| **Failed** | An exception escaped the pipeline. `MediationOutcome.Failed`. | denied, invalid |
| **Canceled** | The caller cancellation token fired. `MediationOutcome.Canceled`, spelled with one `l` to match `OperationCanceledException`. | aborted, stopped |
| **Refusal** | The **category** holding Denied and Invalid, and the only two things it can hold. `Refusal.Denied`, `Refusal.Invalid`, `PipelineDecision.IsRefusal`, `IMessageRefusalMapper`. | (never as a synonym for Denied) |
| **Decision** | What a pre-stage handler returns, normalized to `PipelineDecision`. `PipelineDecision.Continue` lets the pipeline continue. | stop, directive, abort |
| **Reason** | The reason a decision stopped the message, written for a person. `Verdict.Deny(reason)`, `Shortcut.Answer(reason)`, `MessageCompletionContext.Reason`, `AuditRecord.Reason`. | code, message, error |
| **Code** | The machine-readable half of the same decision, meaning the same thing on every shape that carries one: `Verdict`, `Shortcut`, `PipelineDecision`, `Refusal`, `MessageCompletionContext`, `MediationResult`, and `MediationDecision`. Switch on the code rather than matching the reason. | reason, key, error code (on its own) |
| **Evaluate** | Ask what the decision stages would say, without performing the message. `ICommandMediator.EvaluateAsync`, `MediationDecision`. Only guards and validators run. | dry run, check, authorize |
| **Actor** | The person or system that performed an audited action. `AuditActor`, `AuditRecord.Actor`, `IAuditActorResolver`, `IAuditScope.WithActor`. | user, principal, subject, initiator |

The split exists to settle two questions, and the answers are worth stating outright:

- A refusal is never an outcome. `MediationOutcome` has no `Refused` member. Ask `IsRefusal` when you mean "denied or invalid", and switch on `Outcome` when you mean one of them.
- Answering is not denying. A shortcut that skips work an idempotent command already applied has denied nothing, so an audit trail records it as a success. Putting it in the denial list would report a refusal that never happened.

## Auditing

| Term | Definition |
| --- | --- |
| Audit declaration | The constant half of an audit record, stated on the message with `[Audited]`, `[AuditExempt]`, or an `IAuditDefinition<TMessage>`. See [Auditing](../concepts/auditing.md). |
| Audit exemption | A recorded decision that a message is deliberately not audited, carrying the rationale. It is not the same as a message nobody considered, which analyzer LB1018 reports. |
| Audit scope | The variable half of a record, which the handler pushes through `IAuditScope` while it runs. |
| Audit actor | The person or system that performed the action, resolved by an `IAuditActorResolver` at the completion stage so a denied message is attributed too. A null actor means nothing established one, which is a distinct answer from `AuditActor.System`. |
| Audit trail | The sink for finished records (`IAuditTrail`). LiteBus decides when a record is produced and what it holds. Where it is written is your decision. |

## Unit of Work, Idempotency, and Delivery

| Term | Definition |
| --- | --- |
| Unit of work | The application's commit boundary (`IUnitOfWork.CommitAsync`), called once after a successful mediation from a completion handler at `HandlerPriorities.UnitOfWork`. LiteBus decides when it runs. The application owns the transaction. See [Domain Events and Unit of Work](../reliable-messaging/domain-events-and-unit-of-work.md#command-pipeline-boundary). |
| Fault refusal | A fault a stage raised that an `IFaultRefusalClassifier` recognized, so the mediation ends as a denial or an invalid result instead of a failure. See [The Handler Pipeline](../concepts/handler-pipeline.md#turning-faults-into-refusals). |
| Per-handler delivery | Outbox opt-in (`EnablePerHandlerDelivery`) that writes one envelope per subscribed handler, so each reaction retries, dead-letters, and replays alone. See [Outbox](../reliable-messaging/outbox.md#per-handler-delivery). |
| Claim timeout | How long an unsettled idempotency claim blocks its key before a new caller takes it over. The default is five minutes on both shipped stores. See [In-Process Idempotency](../concepts/idempotency.md). |
| Request fingerprint | Content projection stored with an idempotency claim. A repeat with a different fingerprint is denied with `idempotency.fingerprint-mismatch` instead of being answered. See [In-Process Idempotency](../concepts/idempotency.md). |
| Idempotency claim | An atomic insert of a command's key into an `IIdempotencyStore`. The store answers `Granted`, with a claim token fencing the grant, or `AlreadyCompleted`, `InFlight`, or `FingerprintMismatch`. See [In-Process Idempotency](../concepts/idempotency.md). |
| Ambient trace | Trace stamped from the ambient execution context onto a durable envelope under `UseAmbientTrace()` when the item carries no explicit trace. See [Inbox](../reliable-messaging/inbox.md#ambient-trace-opt-in). |
| Authorization declaration | A message's stated authorization position, either authorized under a policy or deliberately exempt with a rationale, enforced by an `AuthorizationGuard<TMessage>`. See [Authorization Declarations](../concepts/authorization-declarations.md). |
| Audit selector | A projection from the message that supplies an audit target or reason when the handler pushed none through `IAuditScope`. See [Auditing](../concepts/auditing.md#declaring-what-the-message-already-knows). |
| Shortcut denial | A refusal returned from the shortcut stage (`Shortcut.Deny`) when only that stage can decide it, ending the mediation as `Denied`. See [The Handler Pipeline](../concepts/handler-pipeline.md#shortcut-contracts). |
| Domain event staging | Draining a scoped `IDomainEventCollector` into `ITransactionalOutbox` at `HandlerPriorities.Persistence`, before the unit-of-work commit. See [Outbox](../reliable-messaging/outbox.md#domain-event-staging). |
| Single-flight lease | A named renewable lease (`IRecurringCommandLease`) that runs a recurring pass on at most one replica. See [Recurring Commands](../integrations/recurring-commands.md#running-on-several-replicas). |

## Reliable Messaging Entry Points

LiteBus keeps domain-specific names instead of one generic "store" verb:

| Term | API | Meaning |
| --- | --- | --- |
| Accept | `IInbox.AcceptAsync` | Store a command-shaped message for later **execution** by the inbox processor. |
| Enqueue | `IOutbox.EnqueueAsync` | Store an event-shaped message for later **publication** by the outbox processor. |
| Schedule | `MessageVisibility` on `InboxAcceptMetadata` / `OutboxEnqueueMetadata` | Accept or enqueue with a future `VisibleAfter` timestamp. |

| Term | Definition |
| --- | --- |
| Inbox | Storage that accepts messages through `IInbox.AcceptAsync` for later execution. See [Inbox](../reliable-messaging/inbox.md). |
| Outbox | Storage that records messages through `IOutbox.EnqueueAsync` for later publication. See [Outbox](../reliable-messaging/outbox.md). |
| Envelope | The stored form of an accepted or enqueued message (`InboxEnvelope` or `OutboxEnvelope`), carrying the contract name and version, payload, lease, and delivery status. See [Inbox](../reliable-messaging/inbox.md). |
| Stable contract | Persisted message identity, made of a name plus an integer version. See [Architecture Decisions](../architecture/decisions.md). |
| Lease | Temporary row ownership so concurrent workers do not process the same envelope twice. |
| Dispatcher | `IInboxDispatcher` or `IOutboxDispatcher`, which executes or publishes a leased envelope. |
| Processor | `PipelinedInboxProcessor` or `PipelinedOutboxProcessor`, which leases rows and invokes dispatchers. |
| At-least-once | Messages may be delivered more than once. Handlers must be idempotent. See [Reliable Messaging](../reliable-messaging/README.md). |
| Dead-letter | Terminal state after retry exhaustion. |
| Store | A persistence adapter that implements the inbox, outbox, or saga roles. See [Reliable Messaging](../reliable-messaging/README.md). |
| Ingress | A broker consumer that accepts deliveries into the inbox. See [Inbox](../reliable-messaging/inbox.md). |

## Registration Vocabulary

| Term | Definition |
| --- | --- |
| `ILiteBusBuilder` | Package-neutral `Modules` entry during `AddLiteBus`. Installed packages add their own `Add*` feature extensions. |
| `Use*` extension | Nested registration on `InboxModuleBuilder` or `OutboxModuleBuilder` (storage, dispatch, ingress). Each maps to one NuGet package. See [Dependency Graph](../architecture/dependency-graph.md). |
| Manifest | `LiteBusHostManifest` listing `IStartupTask`, `IBackgroundService`, and `IDiagnosticCheck` types. See [Hosted services](../architecture/hosted-services.md). |
| `UseInProcessDispatch` | Nested inbox builder extension registering `LiteBus.Inbox.Dispatch.InProcess`, which replays leased envelopes through `ICommandMediator`. See [Inbox](../reliable-messaging/inbox.md). |
| `UseInProcessDispatch` | Nested outbox builder extension registering `LiteBus.Outbox.Dispatch.InProcess`, which publishes leased envelopes through `IEventMediator`. See [Outbox](../reliable-messaging/outbox.md). |

## PostgreSQL Schema Version 1

| Term | Definition |
| --- | --- |
| Schema version | Physical table contract recorded per component. Inbox, outbox, and saga use version **1**. Incompatible historical shapes require the procedures in the [v6 Migration Guide](../migration/v6.md). |
| Create script | `GetCreateScript()` renders current-version DDL for a new table. See [PostgreSQL schema management](../integrations/postgresql-schema-management.md). |
| `EnsureAsync` / `ValidateAsync` | These methods run opt-in host bootstrap or validate-only startup checks for PostgreSQL stores. |

## Delivery Semantics

| Term | Definition |
| --- | --- |
| At-least-once | Processors and brokers may deliver the same message more than once. Handlers must be idempotent. See [Reliable messaging](../reliable-messaging/README.md). |
| Exactly-once effect | Exactly-once effect is your responsibility: idempotency keys, deduplication, and handler design. It is not a broker guarantee. |
| Idempotency key | `InboxAcceptMetadata.Idempotency` or `OutboxEnqueueMetadata.Idempotency` for store-level deduplication on accept or enqueue. |

## Next

- See [Migration](../migration/README.md) for historical version transitions.
- See [The Handler Pipeline](../concepts/handler-pipeline.md) for the stages behind the pipeline words.
