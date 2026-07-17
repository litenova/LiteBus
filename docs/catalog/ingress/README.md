# Inbox Ingress Capability Catalog

Inbox ingress is the durable intake edge for commands. It receives broker deliveries, maps headers to inbox metadata, and writes `InboxEnvelope` rows through `IInbox.AcceptAsync` or `IInbox.AcceptBatchAsync`.

Ingress does not execute handlers. Handler execution starts later in the inbox processor.

## Axis Flow

```mermaid
flowchart LR
  P[Publisher] --> B[Broker destination]
  B --> C[IMessageConsumer]
  C --> L[TransportInboxIngressConsumer]
  L --> H[TransportInboxIngressHandler]
  H --> I[IInbox Accept]
  I --> S[(Inbox store)]
  S --> PR[PipelinedInboxProcessor]
  PR --> D[IInboxDispatcher]
```

## Packages

| Package | Layer | Role |
| --- | --- | --- |
| `LiteBus.Inbox.Ingress` | 4 | Shared handler, consumer loop, mapper, ack policy, telemetry |
| `LiteBus.Inbox.Ingress.Amqp` | 4 | RabbitMQ and LavinMQ ingress adapter |
| `LiteBus.Inbox.Ingress.Kafka` | 4 | Kafka ingress adapter |
| `LiteBus.Inbox.Ingress.AzureServiceBus` | 4 | Service Bus ingress adapter |
| `LiteBus.Inbox.Ingress.AwsSqs` | 4 | SQS ingress adapter |
| `LiteBus.Inbox.Ingress.InMemory` | 4 | In-process ingress adapter for fast tests and local dev |

## Capability Pages

| ID | Name | Maturity |
| --- | --- | --- |
| [ingress.registration](registration.md) | Inbox ingress registration | GA |
| [ingress.transport-handler](transport-handler.md) | Transport-to-inbox accept handler | GA |
| [ingress.transport-consumer](transport-consumer.md) | Ingress background consumer loop | GA |
| [ingress.header-mapping](header-mapping.md) | Wire header to accept metadata | GA |
| [ingress.safety-options](safety-options.md) | Ingress safety and authorization options | GA |
| [ingress.batch-accept](batch-accept.md) | Batch inbox accept buffering | GA |
| [ingress.host-loop](host-loop.md) | Host loop and consumer enablement | GA |
| [ingress.ack-policy](ack-policy.md) | Requeue and discard policy | GA |
| [ingress.telemetry](telemetry.md) | Ingress OpenTelemetry metrics | GA |
| [ingress.amqp](amqp.md) | AMQP inbox ingress | GA |
| [ingress.kafka](kafka.md) | Kafka inbox ingress | Beta |
| [ingress.azure-service-bus](azure-service-bus.md) | Azure Service Bus inbox ingress | Beta |
| [ingress.aws-sqs](aws-sqs.md) | AWS SQS inbox ingress | Beta |
| [ingress.inmemory](inmemory.md) | In-memory inbox ingress | GA |

## Maturity and Rationale

| Broker | Maturity | Why this tier | Main current gaps |
| --- | --- | --- | --- |
| AMQP | GA | Most complete option surface and deepest ingress matrix coverage | Requeue-off poison drain still untested on RabbitMQ/LavinMQ |
| Kafka | Beta | End-to-end and failure paths exist, but option parity is intentionally smaller | No dedicated requeue-off matrix, no dedicated duplicate MessageId matrix, no explicit ack-failed metric assertion |
| AWS SQS | Beta | Strong ingress matrix, including requeue on/off and idempotency paths | Ack-failed metric assertion and FIFO-specific semantics are not covered |
| Azure Service Bus | Beta | End-to-end and failure coverage on emulator, plus requeue-enabled path | No duplicate MessageId matrix, no requeue-off matrix, limited header edge-case matrix |
| InMemory | GA (test adapter) | Fast deterministic matrix for shared ingress semantics | Not a cross-process transport, no broker-native edge semantics |

## Broker Parity Quick View

| Behavior | AMQP | Kafka | AWS SQS | Azure Service Bus | InMemory |
| --- | --- | --- | --- | --- | --- |
| Default `RequeueOnFailure` | true | true | true | true | true |
| Exposes `TrustApplicationHeaders` on broker options | yes | no | no | no | no |
| Exposes batch accept options on broker options | yes | no | no | no | no |
| Broker declaration options on ingress options | yes | no | no | no | no |
| Cross-broker identity and idempotency defaults | broker-scoped | broker-scoped | broker-scoped | broker-scoped | broker-scoped |

## Test Executor Summary

| Executor | Folder scope | What it proves for ingress |
| --- | --- | --- |
| `LiteBus.Inbox.UnitTests` | `Ingress/`, `Ingress/Amqp/` | Mapper, handler, consumer, ack policy, AMQP registration and AMQP handler mapping |
| `LiteBus.Durable.IntegrationTests` | `Ingress/<Broker>/`, `Registration/` | Broker end-to-end intake, failure handling, selected idempotency and requeue behavior, registration smoke |
| `LiteBus.Storage.IntegrationTests` | `PostgreSql/` | AMQP ingress with PostgreSQL backing store and duplicate broker delivery idempotency |

Coverage gaps described in broker pages follow the ingress matrix in [Integration tests](../../testing/integration-tests.md).

## What Ingress Does Not Cover

| Limit | Notes |
| --- | --- |
| Event ingress | Ingress writes commands to inbox. Event publishing stays in outbox dispatch. |
| Exactly-once side effects | Intake is at-least-once with idempotent accept. Handler side effects still need application idempotency where required. |
| Broker health probes | Ingress registers background work. Health probes live in hosting and diagnostics modules. |
| One generic `UseTransportIngress` | Broker adapters stay split by package to avoid pulling every SDK into every app. |

## Deep Docs

- [Inbox](../../reliable-messaging/inbox.md)
- [Integration tests](../../testing/integration-tests.md)
- [AMQP transport](../../integrations/amqp.md)
- [Kafka transport](../../integrations/kafka.md)
- [Azure Service Bus transport](../../integrations/azure-service-bus.md)
- [AWS SQS transport](../../integrations/aws-sqs.md)
- [Reliable messaging semantics](../../reliable-messaging/semantics.md)
