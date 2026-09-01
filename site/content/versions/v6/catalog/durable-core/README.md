# Durable Core Capability Catalog

LiteBus durable messaging persists commands and events before execution or publication, then replays them through background processors with at-least-once delivery, leases, retries, and operator tooling. This catalog maps each capability to its public surface, packages, invariants, observability, and test coverage.

**Scope:** inbox, outbox, reliable semantics, transactional writes, domain events, command-inbox patterns, envelope lifecycle, processors, lease/retry/dead-letter, idempotency, and scheduling metadata. Broker-specific transport guides live outside this axis; dispatch and ingress capabilities here describe how durable core connects to transport adapters.

**Catalog sections (per capability file):**

| Section | Content |
| --- | --- |
| **Public surface** | Consumer contracts, invocation, registration, configuration, and extension points |
| **Observability** | Per-instrument detail: name, kind, when emitted, tags, enablement, operational notes |
| **Test coverage** | One block per automated test method (use case, kind, description, behavior, outcome, remarks); untested and out-of-scope gaps remain as tables |

**Test coverage research:** Tests under `tests/LiteBus.Inbox.UnitTests`, `tests/LiteBus.Outbox.UnitTests`, `tests/LiteBus.Durable.IntegrationTests` (`Ingress/`, `Dispatch/`, `Registration/`), `tests/LiteBus.Enterprise*`, and `tests/LiteBus.Storage*`.

**Maturity legend**

| Tier | Meaning |
| --- | --- |
| GA | Production-ready; stable public contract |
| Beta | Shipped; exercise in non-critical paths first |
| Extension | Optional package on top of durable core |
| Planned | Documented on [Roadmap](../../roadmap/README.md); not shipped |

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

## Deep Docs

| Topic | Doc |
| --- | --- |
| Inbox reference | [Inbox](../../reliable-messaging/inbox.md) |
| Outbox reference | [Outbox](../../reliable-messaging/outbox.md) |
| Delivery guarantees | [Reliable messaging semantics](../../reliable-messaging/semantics.md) |
| Atomic domain + store | [Transactional messaging writes](../../reliable-messaging/transactional-writes.md) |
| Domain events pattern | [Domain events and unit of work](../../concepts/domain-events-and-unit-of-work.md) |
| Architecture | [Architecture](../../architecture/README.md) (durable sections) |
| Roadmap | [Roadmap](../../roadmap/README.md) |
