# Dispatch Axis Capability Catalog

Dispatch adapters turn a leased durable envelope into a side effect. Inbox processors call `IInboxDispatcher.DispatchAsync`; outbox processors call `IOutboxDispatcher.DispatchAsync`. The processor persists outcome state after dispatch returns or throws.

The dispatch axis sits between durable processing and execution targets. It never accepts new messages, never owns store schema, and never decides retry budgets. Those responsibilities belong to ingress, storage, and durable core capabilities.

## Axis Overview

```text
Inbox storage lease
  -> PipelinedInboxProcessor
  -> IInboxDispatcher
     -> in-process command mediator
     -> transport publish (AMQP, Azure Service Bus, AWS SQS, Kafka, InMemory)

Outbox storage lease
  -> PipelinedOutboxProcessor
  -> IOutboxDispatcher
     -> in-process event mediator
     -> transport publish with canonical headers
```

Register exactly one dispatcher per inbox or outbox module. Processor background services fail at startup when no dispatcher is registered (LB1014 at compile time when the pattern is detectable).

## Dependency Diagram

```text
dispatch.registration
  -> dispatch.processor-coupling
  -> dispatch.transport-core
     -> dispatch.transport-envelope-mapping
     -> dispatch.inbox.amqp / dispatch.outbox.amqp
     -> dispatch.inbox.azure-service-bus / dispatch.outbox.azure-service-bus
     -> dispatch.inbox.aws-sqs / dispatch.outbox.aws-sqs
     -> dispatch.inbox.kafka / dispatch.outbox.kafka
     -> dispatch.inbox.inmemory / dispatch.outbox.inmemory
  -> dispatch.inbox.in-process
  -> dispatch.outbox.in-process
```

## Capability Index

| ID | Name | Maturity |
| --- | --- | --- |
| [dispatch.registration](registration.md) | Dispatcher registration | GA |
| [dispatch.processor-coupling](processor-coupling.md) | Processor pipeline coupling | GA |
| [dispatch.transport-core](transport-core.md) | Shared transport dispatchers | GA |
| [dispatch.transport-envelope-mapping](transport-envelope-mapping.md) | Envelope-to-wire header mapping | GA |
| [dispatch.inbox.in-process](inbox-in-process.md) | Inbox in-process command dispatch | GA |
| [dispatch.outbox.in-process](outbox-in-process.md) | Outbox in-process event dispatch | GA |
| [dispatch.inbox.amqp](inbox-amqp.md) | Inbox AMQP dispatch | GA |
| [dispatch.outbox.amqp](outbox-amqp.md) | Outbox AMQP dispatch | GA |
| [dispatch.inbox.azure-service-bus](inbox-azure-service-bus.md) | Inbox Azure Service Bus dispatch | GA |
| [dispatch.outbox.azure-service-bus](outbox-azure-service-bus.md) | Outbox Azure Service Bus dispatch | GA |
| [dispatch.inbox.aws-sqs](inbox-aws-sqs.md) | Inbox AWS SQS dispatch | GA |
| [dispatch.outbox.aws-sqs](outbox-aws-sqs.md) | Outbox AWS SQS dispatch | GA |
| [dispatch.inbox.kafka](inbox-kafka.md) | Inbox Kafka dispatch | GA |
| [dispatch.outbox.kafka](outbox-kafka.md) | Outbox Kafka dispatch | GA |
| [dispatch.inbox.inmemory](inbox-inmemory.md) | Inbox InMemory transport dispatch | GA |
| [dispatch.outbox.inmemory](outbox-inmemory.md) | Outbox InMemory transport dispatch | GA |

## Catalog Page Template

Each capability page includes:

- **Header**: ID, Name, Maturity, Summary
- **What it does**: behavior and deployment patterns
- **Public surface**: registration extensions, options tables, key methods
- **Packages / Requires / Invariants / Non-goals**
- **Observability**: activity names, meter names, instrument names, tags
- **Test coverage**: Covered, Untested, Out-of-scope
- **Deep docs**: long-form docs and integration test matrix links

Primary test projects referenced across capability pages:

| Project | Folders | Role |
| --- | --- | --- |
| `LiteBus.Durable.IntegrationTests` | `Dispatch/Inbox/*`, `Dispatch/Outbox/*`, `Registration/` | Broker dispatch end-to-end and registration matrix |
| `LiteBus.Inbox.UnitTests` | `Dispatch/`, `Dispatch/InProcess/` | Inbox dispatcher and mapper unit coverage |
| `LiteBus.Outbox.UnitTests` | `Dispatch/`, `Dispatch/InProcess/` | Outbox dispatcher and mapper unit coverage |
| `LiteBus.Storage.IntegrationTests` | `PostgreSql/` | Durable chain behavior through PostgreSQL storage |

## Related Docs

- [Architecture: Dispatch axis](../../architecture/README.md#dispatch-axis)
- [Inbox: Dispatch extensions](../../reliable-messaging/inbox.md#dispatch-use-extensions)
- [Outbox: Dispatch extensions](../../reliable-messaging/outbox.md#dispatch-use-extensions)
- [AMQP transport guide](../../integrations/amqp.md)
- [Azure Service Bus transport guide](../../integrations/azure-service-bus.md)
- [AWS SQS transport guide](../../integrations/aws-sqs.md)
- [Kafka transport guide](../../integrations/kafka.md)
- [Dependency graph: Dispatch packages](../../architecture/dependency-graph.md)
- [Integration tests: Dispatch matrix](../../testing/integration-tests.md)
- [v6 feature index: Dispatch row](../../reference/feature-index-v6.md)
