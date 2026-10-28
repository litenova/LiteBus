# Durable Core

LiteBus durable messaging stores commands and events before it runs or publishes them. Background processors then replay the stored messages with at-least-once delivery, using leases, retries, and operator tooling. This catalog is the reference for contributors to that axis. It maps each capability to its public surface, packages, invariants, observability, and test coverage. Readers using LiteBus start with [the documentation overview](../../index.md).

The scope covers the inbox, the outbox, reliable semantics, transactional writes, domain events, command-inbox patterns, the envelope lifecycle, processors, lease, retry, and dead-letter handling, idempotency, and scheduling metadata. Broker-specific transport guides sit outside this axis. The dispatch and ingress capabilities here describe how durable core connects to transport adapters.

Each capability file has the following sections:

| Section | Content |
| --- | --- |
| **Public surface** | Consumer contracts, invocation, registration, configuration, and extension points |
| **Observability** | For each instrument, the name, kind, when it is emitted, tags, enablement, and operational notes |
| **Test coverage** | One block per automated test method, with its use case, kind, description, behavior, outcome, and remarks. Untested and out-of-scope gaps appear as tables |

The test coverage sections draw on tests under `tests/LiteBus.Inbox.UnitTests`, `tests/LiteBus.Outbox.UnitTests`, `tests/LiteBus.Durable.IntegrationTests` (`Ingress/`, `Dispatch/`, `Registration/`), `tests/LiteBus.Enterprise*`, and `tests/LiteBus.Storage*`.

The maturity column uses these tiers:

| Tier | Meaning |
| --- | --- |
| GA | Production-ready, with a stable public contract |
| Beta | Shipped. Try it in non-critical paths first |
| Extension | Optional package on top of durable core |
| Planned | Documented on [Roadmap](../../roadmap/README.md) but not shipped |

## Capabilities

| ID | Capability | Maturity |
| --- | --- | --- |
| [durable-core.inbox-acceptance](inbox-acceptance.md) | Inbox acceptance | GA |
| [durable-core.outbox-enqueue](outbox-enqueue.md) | Outbox enqueue | GA |
| [durable-core.reliable-messaging-semantics](reliable-messaging-semantics.md) | Reliable messaging semantics | GA |
| [durable-core.transactional-writes](transactional-writes.md) | Transactional inbox/outbox writes | GA |
| [durable-core.domain-events-unit-of-work](domain-events-unit-of-work.md) | Domain events and unit of work | GA |
| [durable-core.command-inbox-patterns](command-inbox-patterns.md) | Command-inbox patterns | GA |
| [durable-core.message-contracts](message-contracts.md) | Durable message contracts | GA |
| [durable-core.envelope-lifecycle](envelope-lifecycle.md) | Envelope lifecycle | GA |
| [durable-core.inbox-processor](inbox-processor.md) | Inbox processor | GA |
| [durable-core.outbox-processor](outbox-processor.md) | Outbox processor | GA |
| [durable-core.lease-retry-dead-letter](lease-retry-dead-letter.md) | Lease, retry, and dead letter | GA |
| [durable-core.idempotency](idempotency.md) | Acceptance idempotency | GA |
| [durable-core.scheduling-metadata](scheduling-metadata.md) | Scheduling and visibility metadata | GA |
| [durable-core.durable-storage](durable-storage.md) | Durable storage | GA |
| [durable-core.durable-dispatch](durable-dispatch.md) | Durable dispatch | GA |
| [durable-core.inbox-ingress](inbox-ingress.md) | Inbox ingress | GA (AMQP); Beta (Kafka, AWS SQS, Azure Service Bus) |
| [durable-core.operations-management](operations-management.md) | Operations and management | GA |
| [durable-core.payload-encryption](payload-encryption.md) | Payload encryption at rest | GA |
| [durable-core.tenant-scoping](tenant-scoping.md) | Tenant scoping | GA |
| [durable-core.processor-hooks](processor-hooks.md) | Processor envelope hooks | GA / Extension (Saga) |

## References

| Topic | Doc |
| --- | --- |
| Inbox reference | [Inbox](../../reliable-messaging/inbox.md) |
| Outbox reference | [Outbox](../../reliable-messaging/outbox.md) |
| Delivery guarantees | [Reliable messaging semantics](../../reliable-messaging/semantics.md) |
| Atomic domain + store | [Transactional messaging writes](../../reliable-messaging/transactional-writes.md) |
| Domain events pattern | [Domain events and unit of work](../../reliable-messaging/domain-events-and-unit-of-work.md) |
| Architecture | [Architecture](../../architecture/README.md) (durable sections) |
| Roadmap | [Roadmap](../../roadmap/README.md) |
