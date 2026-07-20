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
| Pipeline | Pre-handlers, main handler, post-handlers, and error-handlers for one message. See [The Handler Pipeline](../concepts/handler-pipeline.md). |
| Pre-handler | Runs before the main handler. |
| Post-handler | Runs after the main handler. |
| Error-handler | Runs when a stage throws. |
| Execution context | Ambient per-mediation state (`AsyncLocal`). See [Execution Context](../concepts/execution-context.md). |

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
