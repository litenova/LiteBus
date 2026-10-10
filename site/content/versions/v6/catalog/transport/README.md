# Transport Axis Capability Catalog

LiteBus transport is the broker-facing publish and consume axis. Dispatch and ingress call transport contracts (`ITransportPublisher`, `IMessageConsumer`) but own durable storage, leasing, and processor policy.

## Role in LiteBus

```text
Outbox and inbox dispatch   -> ITransportPublisher.PublishAsync
Inbox ingress adapters      -> IMessageConsumer.StartAsync
Raw transport hosts         -> IMessageConsumer + TransportConsumerHandlerInvoker
```

Transport maps durable envelope metadata to canonical wire headers. It does not write inbox rows, lease envelopes, or run command and event mediators.

## Capability Pages and Quality Bar

Every page in this folder is a reference contract and uses the same required sections:

| Section | Required content |
| --- | --- |
| ID, Name, Maturity, Summary | Stable capability identity and tier |
| What it does | Runtime behavior and boundaries |
| Public surface | API tables with concrete types and members |
| Packages | Package and adapter ownership |
| Requires | Capability dependencies |
| Invariants | Behavior that must remain stable |
| Non-goals | Explicit exclusions |
| Observability | Verified meter, activity, and tag names from `LiteBus.Transport*` |
| Test coverage | Covered, Untested, and Out-of-scope |
| Deep docs | Axis and feature guides |

`manual-acknowledgement.md` and `ingress/amqp.md` remain the depth benchmark for all pages.

## Packages

| Package | Dependency role | Purpose |
| --- | --- | --- |
| `LiteBus.Transport.Abstractions` | Platform contracts | Contracts, canonical headers, ack model |
| `LiteBus.Transport` | Core implementation | Circuit breaker, tracing, header mapper, invoker |
| `LiteBus.Transport.Amqp` | Technology adapter | RabbitMQ and LavinMQ adapter |
| `LiteBus.Transport.Kafka` | Technology adapter | Kafka adapter |
| `LiteBus.Transport.AwsSqs` | Technology adapter | AWS SQS adapter |
| `LiteBus.Transport.AzureServiceBus` | Technology adapter | Azure Service Bus adapter |
| `LiteBus.Transport.InMemory` | Technology adapter | In-process channel adapter |
| `LiteBus.Transport.Extensions.OpenTelemetry` | Host adapter | `AddLiteBusTransportMetrics()` |
| `LiteBus.Transport.Amqp.Extensions.OpenTelemetry` | Host adapter | `AddLiteBusAmqpMetrics()` alias |

## Capability Index

| ID | Name | Maturity | File |
| --- | --- | --- | --- |
| `transport.publish-consume-contracts` | Publish and consume contracts | GA | [publish-consume-contracts.md](publish-consume-contracts.md) |
| `transport.manual-acknowledgement` | Manual acknowledgement model | GA | [manual-acknowledgement.md](manual-acknowledgement.md) |
| `transport.canonical-headers` | Canonical wire headers | GA | [canonical-headers.md](canonical-headers.md) |
| `transport.envelope-header-mapping` | Envelope to header mapping | GA | [envelope-header-mapping.md](envelope-header-mapping.md) |
| `transport.circuit-breaker` | Transport circuit breaker | GA | [circuit-breaker.md](circuit-breaker.md) |
| `transport.consumer-handler-invoker` | Consumer handler invoker | GA | [consumer-handler-invoker.md](consumer-handler-invoker.md) |
| `transport.single-broker-registration` | Single broker registration | GA | [single-broker-registration.md](single-broker-registration.md) |
| `transport.tenant-routing` | Tenant routing strategy | GA | [tenant-routing.md](tenant-routing.md) |
| `transport.tracing` | Publish and consume tracing | GA | [tracing.md](tracing.md) |
| `transport.metrics` | Circuit breaker metrics | GA | [metrics.md](metrics.md) |
| `transport.amqp` | AMQP transport | GA | [amqp.md](amqp.md) |
| `transport.kafka` | Kafka transport | GA | [kafka.md](kafka.md) |
| `transport.azure-service-bus` | Azure Service Bus transport | Beta | [azure-service-bus.md](azure-service-bus.md) |
| `transport.aws-sqs` | AWS SQS transport | Beta | [aws-sqs.md](aws-sqs.md) |
| `transport.inmemory` | In-memory transport | GA | [inmemory.md](inmemory.md) |

## Four-Executor Test Pattern

Transport reference pages use a four-executor test map:

| Executor | Scope |
| --- | --- |
| `LiteBus.Transport.UnitTests` | Adapter and core unit behavior (`Amqp/`, `Kafka/`, `AwsSqs/`, `AzureServiceBus/`, `InMemory/`) |
| `LiteBus.Transport.IntegrationTests` | AMQP transport wire tests |
| `LiteBus.Durable.IntegrationTests` | Dispatch and ingress hops that exercise transport through durable pipelines |
| Application host executor | Consumer application integration that composes LiteBus modules; repo helper package `LiteBus.Transport.IntegrationTesting` provides fixtures only and is not an executor |

Custom adapter authors can reference the published `LiteBus.Transport.Testing` package and derive from `TransportContractTests`. The broker-neutral suite verifies payload and header round trips, explicit redelivery, and cancellation against the adapter's real broker.

## Shared Observability Contract

Transport circuit breaker metrics are emitted on meter `LiteBus.Transport` and tagged by `litebus.transport.broker`.

| Instrument | Public constant | Value semantics |
| --- | --- | --- |
| `litebus.transport.circuit_breaker.open` | `LiteBusTransportTelemetry.CircuitBreakerOpenInstrumentName` | `1` when any publisher circuit is open or half-open; otherwise `0` |
| `litebus.transport.circuit_breaker.failure_count` | `LiteBusTransportTelemetry.CircuitBreakerFailureCountInstrumentName` | Sum of current failures across destination-scoped publisher circuits |

Broker tag values are `amqp`, `kafka`, `sqs`, `azure_service_bus`, and `inmemory`.

## Deep Docs

| Topic | Location |
| --- | --- |
| Transport platform overview | [Architecture.md](../../architecture/README.md) |
| Package inventory and dependency roles | [Dependency-Graph.md](../../architecture/dependency-graph.md) |
| AMQP transport guide | [Amqp-Transport.md](../../integrations/amqp.md) |
| Kafka transport guide | [Kafka-Transport.md](../../integrations/kafka.md) |
| AWS SQS transport guide | [Aws-Sqs-Transport.md](../../integrations/aws-sqs.md) |
| Azure Service Bus transport guide | [Azure-Service-Bus-Transport.md](../../integrations/azure-service-bus.md) |
| Integration test executors | [Integration-Tests.md](../../testing/integration-tests.md) |
