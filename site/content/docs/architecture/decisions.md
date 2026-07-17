# Architecture Decisions

These records capture design choices that should remain stable unless a future major version changes the project boundary.

## ADR-0001: Commands, Queries, and Events Stay Separate

Status: Accepted

LiteBus keeps `ICommand`, `IQuery`, and `IEvent` as separate concepts. Collapsing them into a single request abstraction would remove the semantic API that distinguishes LiteBus from generic mediator libraries.

## ADR-0002: Events Can Be POCOs

Status: Accepted

Events can be plain CLR objects. Domain projects can define events without referencing LiteBus.

## ADR-0003: LiteBus Is In-Process

Status: Accepted

LiteBus mediates work inside a process. Inbox and outbox packages store work and facts; LiteBus does not become a distributed message broker. Broker adapters live in optional `LiteBus.Transport.*` and dispatch/ingress packages.

## ADR-0004: The Inbox Defers Execution

Status: Accepted

The inbox stores messages for later execution. It does not return handler results and does not replace an outbox. Inbox submission uses `IInbox.AcceptAsync`; `ICommandMediator.SendAsync` always executes in process. `UseInProcessDispatch` validates `ICommand` at execution time.

## ADR-0005: The Outbox Is Separate from Event Mediation

Status: Accepted

Event mediation dispatches handlers in process. An outbox records facts that must be published after a state change. `IEventMediator.PublishAsync` notifies in-process handlers now; `IOutbox.EnqueueAsync` stores a message for later publication through `IOutboxDispatcher`.

## ADR-0006: Ambient Context Uses AsyncLocal

Status: Accepted

The execution context uses ambient async flow so pipeline stages share cancellation, tags, and `Items` without extra handler parameters.

## ADR-0007: DI Integration Is Adapter-Based

Status: Accepted

The runtime owns a dependency registration abstraction. Microsoft DI and Autofac adapters translate LiteBus descriptors into container registrations.

## ADR-0008: Persisted Messages Use Stable Contracts

Status: Accepted

Inbox and outbox storage use stable contract names and integer versions, not assembly-qualified CLR names in persisted rows.

## ADR-0009: v6 Greenfield Schema

Status: Accepted

PostgreSQL inbox/outbox/saga schemas reset to version **1** with the full final column set in the initial create. No in-place upgrade from LiteBus v5 table shapes. Deployments drop legacy tables and apply v1 DDL.

## ADR-0010: Pipelined Processors Only

Status: Accepted

`PipelinedInboxProcessor` and `PipelinedOutboxProcessor` are the only processor implementations. Sequential legacy loops are removed.

## ADR-0011: Saga Decoupled via Orchestration

Status: Accepted

Saga hooks implement `IProcessorEnvelopeHook` from `LiteBus.DurableMessaging.Abstractions`. Registration stays on `inbox.EnableSaga()`; inbox maps envelopes through an adapter.

## Next

See [Glossary](../reference/glossary.md) for vocabulary.
