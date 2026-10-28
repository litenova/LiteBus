# Architecture Decisions

LiteBus architecture decisions record design choices that stay stable unless a future major version changes the project boundary. This page lists each decision with its status.

## ADR-0001: Commands, Queries, and Events Stay Separate

Status: Accepted

LiteBus keeps `ICommand`, `IQuery`, and `IEvent` as separate concepts. Collapsing them into a single request abstraction would remove the semantic API that distinguishes LiteBus from generic mediator libraries.

## ADR-0002: Events Can Be POCOs

Status: Accepted

Events can be plain CLR objects. Domain projects can define events without referencing LiteBus.

## ADR-0003: LiteBus Is In-Process

Status: Accepted

LiteBus mediates work inside a process. Inbox and outbox packages store work and facts. LiteBus does not become a distributed message broker. Broker adapters live in optional `LiteBus.Transport.*` and dispatch/ingress packages.

## ADR-0004: The Inbox Defers Execution

Status: Accepted

The inbox stores messages for later execution. It does not return handler results and does not replace an outbox. Inbox submission uses `IInbox.AcceptAsync`. `ICommandMediator.SendAsync` always executes in process. `UseInProcessDispatch` validates `ICommand` at execution time.

## ADR-0005: The Outbox Is Separate from Event Mediation

Status: Accepted

Event mediation dispatches handlers in process. An outbox records facts that must be published after a state change. `IEventMediator.PublishAsync` notifies in-process handlers now. `IOutbox.EnqueueAsync` stores a message for later publication through `IOutboxDispatcher`.

## ADR-0006: Ambient Context Uses AsyncLocal

Status: Accepted

The execution context flows with async execution. Pipeline stages therefore share cancellation, tags, and `Items` without extra handler parameters.

## ADR-0007: DI Integration Is Adapter-Based

Status: Accepted

The runtime owns a dependency registration abstraction. Microsoft DI and Autofac adapters translate LiteBus descriptors into container registrations.

## ADR-0008: Persisted Messages Use Stable Contracts

Status: Accepted

Inbox and outbox storage use stable contract names and integer versions, not assembly-qualified CLR names in persisted rows.

## ADR-0009: Schema Version 1

Status: Accepted

PostgreSQL inbox, outbox, and saga schemas use version 1. The version 1 create scripts contain the complete current table shape. The schema manager validates that shape and does not mutate incompatible existing tables. Historical database transitions are documented in the [v6 Migration Guide](../migration/v6.md).

## ADR-0010: Pipelined Processors Only

Status: Accepted

`PipelinedInboxProcessor` and `PipelinedOutboxProcessor` are the only processor implementations. LiteBus does not register an alternate sequential processor.

## ADR-0011: Saga Decoupled via Orchestration

Status: Accepted

Saga hooks implement `IProcessorEnvelopeHook` from `LiteBus.Messaging.Abstractions.Processing`. Registration runs through `inbox.EnableSaga()`. Inbox maps envelopes through an adapter.

## Next

See [Glossary](../reference/glossary.md) for vocabulary.
