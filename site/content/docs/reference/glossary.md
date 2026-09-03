# Glossary

This page defines terms used across the LiteBus documentation. Each entry links to the page that explains it in depth.

## Core Concepts

| Term | Definition |
| --- | --- |
| Mediator | The in-process component that routes a message to its handlers. LiteBus exposes typed mediators (`ICommandMediator`, `IQueryMediator`, `IEventMediator`) over a shared `IMessageMediator`. See [Architecture](../architecture/README.md). |
| Command | A message expressing an intention to change state. Resolves to exactly one main handler. See [Command Module](../concepts/commands.md). |
| Query | A message that reads state and returns a result. Resolves to exactly one main handler. See [Query Module](../concepts/queries.md). |
| Event | A message reporting a fact. Broadcasts to every matching handler, or none. See [Event Module](../concepts/events.md). |
| Handler | A class that processes a message. The single class that does the work is the main handler. |

## Pipeline

| Term | Definition |
| --- | --- |
| Pipeline | The pre stage, main handler, post-handlers, error-handlers, and completion handlers for one message. See [The Handler Pipeline](../concepts/handler-pipeline.md). |
| Pre stage | The whole phase before the main handler. It holds four roles: guard, validator, shortcut, and pre-handler. Named by the `PreStage` enum. |
| Guard | Decides whether the message is permitted to proceed. Returns `Verdict`. |
| Validator | Decides whether the message is well-formed. Returns `Validity`. |
| Shortcut | Decides whether the answer is already known. Returns `Shortcut` or `Shortcut<TResult>`. |
| Pre-handler | The fourth pre-stage role, and the one LiteBus does not name after a job: it prepares a message that is going to be handled and cannot end the pipeline by returning. |
| Main handler | The class that does the work. One for a command or query, zero to many for an event. |
| Post-handler | Runs after the main handler succeeded. |
| Error-handler | Runs when a stage throws a recoverable exception. |
| Completion handler | Runs once at the end of every mediation, on every path, and observes how it ended. |
| Refusal mapper | Not a stage. Turns a denial or a validation failure into the result the caller receives, in place of raising. |
| Execution context | Ambient per-mediation state (`AsyncLocal`). See [Execution Context](../concepts/execution-context.md). |

## Pipeline Vocabulary

These words each name exactly one thing, and nothing else in LiteBus is called by them. Where the documentation, an XML comment, and a type name could disagree, this table is the one that wins. The rules that decide why each word was chosen, and how to name the next one, are in [Mediation Layer Design Rules](../architecture/mediation-design.md).

| Word | Means | Do not write |
| --- | --- | --- |
| **Denied** | A guard refused the message. `Verdict.Deny`, `MediationOutcome.Denied`, `AuditOutcome.Denied`, `LiteBusMessageDeniedException`. | rejected, refused (on its own) |
| **Invalid** | A validator found the message malformed. `Validity.Invalid`, `MediationOutcome.Invalid`, `AuditOutcome.Invalid`, `LiteBusMessageInvalidException`. | denied, rejected, failed |
| **Answered** | A shortcut supplied the result, so the main handler never ran. `Shortcut.Answer`, `MediationOutcome.Answered`, recorded as `AuditOutcome.Succeeded`. | short-circuited, skipped, cancelled |
| **Failed** | An exception escaped the pipeline. `MediationOutcome.Failed`. | denied, invalid |
| **Canceled** | The caller's cancellation token fired. `MediationOutcome.Canceled`, spelled with one `l` to match `OperationCanceledException`. | aborted, stopped |
| **Refusal** | The **category** holding Denied and Invalid, and the only two things it can hold. `Refusal.Denied`, `Refusal.Invalid`, `PipelineDecision.IsRefusal`, `IMessageRefusalMapper`. | (never as a synonym for Denied) |
| **Decision** | What a pre-stage handler returns, normalized to `PipelineDecision`. `PipelineDecision.Continue` lets the pipeline run on. | stop, directive, abort |
| **Reason** | Why a decision stopped the message, written for a person. `Verdict.Deny(reason)`, `Shortcut.Answer(reason)`, `MessageCompletionContext.Reason`, `AuditRecord.Reason`. | code, message, error |
| **Code** | The machine-readable half of the same decision, meaning the same thing on every shape that carries one: `Verdict`, `Shortcut`, `PipelineDecision`, `Refusal`, `MessageCompletionContext`, `MediationResult`, `MediationDecision`. Switch on it rather than matching the reason. | reason, key, error code (on its own) |
| **Evaluate** | Ask what the decision stages would say, without performing the message. `ICommandMediator.EvaluateAsync`, `MediationDecision`. Runs guards and validators only. | dry run, check, authorize |
| **Actor** | Who performed an audited action. `AuditActor`, `AuditRecord.Actor`, `IAuditActorResolver`, `IAuditScope.WithActor`. | user, principal, subject, initiator |

Two consequences worth stating outright, because they are the questions the split exists to answer:

- A refusal is never an outcome. `MediationOutcome` has no `Refused` member. Ask `IsRefusal` when you mean "denied or invalid", and switch on `Outcome` when you mean one of them.
- Answering is not denying. A shortcut that skips work an idempotent command already applied denied nobody, so an audit trail records it as a success. Putting it in the denial list would report a refusal that never happened.

## Auditing

| Term | Definition |
| --- | --- |
| Audit declaration | The constant half of an audit record, stated on the message with `[Audited]`, `[AuditExempt]`, or an `IAuditDefinition<TMessage>`. See [Auditing](../concepts/auditing.md). |
| Audit exemption | A recorded decision that a message is deliberately not audited, carrying the rationale. Not the same as a message nobody considered, which analyzer LB1018 reports. |
| Audit scope | The variable half of a record, pushed by the handler while it runs through `IAuditScope`. |
| Audit actor | Who performed the action, resolved by an `IAuditActorResolver` at the completion stage so a denied message is attributed too. A null actor means nothing established one, which is a distinct answer from `AuditActor.System`. |
| Audit trail | The application's sink for finished records (`IAuditTrail`). LiteBus decides when a record is produced and what it holds; where it is written is the application's decision. |

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
| Stable contract | Persisted message identity: name + integer version. See [Architecture Decisions](../architecture/decisions.md). |
| Lease | Temporary row ownership so concurrent workers do not process the same envelope twice. |
| Dispatcher | `IInboxDispatcher` or `IOutboxDispatcher`; executes or publishes a leased envelope. |
| Processor | `PipelinedInboxProcessor` or `PipelinedOutboxProcessor`; leases rows and invokes dispatchers. |
| At-least-once | Messages may be delivered more than once; handlers must be idempotent. See [Reliable Messaging](../reliable-messaging/README.md). |
| Dead-letter | Terminal state after retry exhaustion. |

## Registration Vocabulary

| Term | Definition |
| --- | --- |
| `ILiteBusBuilder` | Package-neutral `Modules` entry during `AddLiteBus`; installed packages add normal `Add*` feature extensions. |
| `Use*` extension | Nested registration on `InboxModuleBuilder` / `OutboxModuleBuilder` (storage, dispatch, ingress). Each maps to one NuGet package. See [Dependency Graph](../architecture/dependency-graph.md). |
| Manifest | `LiteBusHostManifest` listing `IStartupTask`, `IBackgroundService`, and `IDiagnosticCheck` types. See [Hosted services](../architecture/hosted-services.md). |
| `UseInProcessDispatch` | Nested inbox builder extension registering `LiteBus.Inbox.Dispatch.InProcess`; replays leased envelopes through `ICommandMediator`. See [Inbox](../reliable-messaging/inbox.md). |
| `UseInProcessDispatch` | Nested outbox builder extension registering `LiteBus.Outbox.Dispatch.InProcess`; publishes leased envelopes through `IEventMediator`. See [Outbox](../reliable-messaging/outbox.md). |

## PostgreSQL Schema Version 1

| Term | Definition |
| --- | --- |
| Schema version | Physical table contract recorded per component. Inbox, outbox, and saga use version **1**. Incompatible historical shapes require the procedures in the [Migration Guide](../migration/v6.md). |
| Create script | `GetCreateScript()` renders current-version DDL for a new table. See [PostgreSQL schema management](../integrations/postgresql-schema-management.md). |
| `EnsureAsync` / `ValidateAsync` | Opt-in host bootstrap or validate-only startup for PostgreSQL stores. |

## Delivery Semantics

| Term | Definition |
| --- | --- |
| At-least-once | Processors and brokers may deliver the same message more than once. Handlers must be idempotent. See [Reliable messaging](../reliable-messaging/README.md). |
| Exactly-once effect | Application responsibility: idempotency keys, deduplication, and handler design; not a broker guarantee. |
| Idempotency key | `InboxAcceptMetadata.Idempotency` / `OutboxEnqueueMetadata.Idempotency` for store-level deduplication on accept/enqueue. |

## Next

See [Migration Guides](../migration/README.md) for historical version transitions.
