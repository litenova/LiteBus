# Feature Index

Quick map from capability to documentation and primary packages. For install commands, see the [Documentation Index](https://litebus.io/docs). For the full cross-axis inventory, see [Capability Catalog](capability-catalog.md).

## Mediator

| Capability | Doc | Packages |
| --- | --- | --- |
| Commands | [Command module](../concepts/commands.md) | `LiteBus.Commands`, `*.Extensions.Microsoft.DependencyInjection` |
| Queries | [Query module](../concepts/queries.md) | `LiteBus.Queries`, `*.Extensions.Microsoft.DependencyInjection` |
| Events | [Event module](../concepts/events.md) | `LiteBus.Events`, `*.Extensions.Microsoft.DependencyInjection` |
| Handler pipeline | [The handler pipeline](../concepts/handler-pipeline.md) | Core mediator packages |
| Execution context | [Execution context](../concepts/execution-context.md) | `LiteBus.Messaging.Abstractions` |

## Durable Messaging Core

| Capability | Doc | Writer API |
| --- | --- | --- |
| Inbox accept + processor | [Inbox](../reliable-messaging/inbox.md) | `IInbox.AcceptAsync(InboxAcceptItem)` |
| Outbox enqueue + processor | [Outbox](../reliable-messaging/outbox.md) | `IOutbox.EnqueueAsync(OutboxEnqueueItem)` |
| Delivery semantics | [Reliable messaging semantics](../reliable-messaging/semantics.md) | Metadata on `*Item` |
| Atomic domain + durable writes | [Transactional messaging writes](../reliable-messaging/transactional-writes.md) | `ITransactionalInbox` / `ITransactionalOutbox` |
| Domain events to outbox | [Domain events and unit of work](../concepts/domain-events-and-unit-of-work.md) | `OutboxEnqueueMetadata` |

## Storage

| Store | Inbox package | Outbox package |
| --- | --- | --- |
| PostgreSQL | `LiteBus.Inbox.Storage.PostgreSql` | `LiteBus.Outbox.Storage.PostgreSql` |
| Entity Framework Core | `LiteBus.Inbox.Storage.EntityFrameworkCore` | `LiteBus.Outbox.Storage.EntityFrameworkCore` |
| InMemory | `LiteBus.Inbox.Storage.InMemory` | `LiteBus.Outbox.Storage.InMemory` |

Shared PostgreSQL primitives: `LiteBus.Storage.PostgreSql`.

## Dispatch

Broker dispatch adapters ship for every transport package. The [Documentation Index](https://litebus.io/docs) records the release classification for each transport, dispatch, and ingress surface.

| Path | Inbox | Outbox |
| --- | --- | --- |
| In-process | `UseInProcessDispatch()` | `UseInProcessDispatch()` |
| AMQP | `LiteBus.Inbox.Dispatch.Amqp` | `LiteBus.Outbox.Dispatch.Amqp` |
| Azure Service Bus | `*.Dispatch.AzureServiceBus` | `*.Dispatch.AzureServiceBus` |
| AWS SQS | `*.Dispatch.AwsSqs` | `*.Dispatch.AwsSqs` |
| Kafka | `*.Dispatch.Kafka` | `*.Dispatch.Kafka` |
| InMemory | `*.Dispatch.InMemory` | `*.Dispatch.InMemory` |

## Ingress

| Broker | Tier | Package |
| --- | --- | --- |
| AMQP | GA | `LiteBus.Inbox.Ingress.Amqp` |
| Azure Service Bus | Beta | `LiteBus.Inbox.Ingress.AzureServiceBus` |
| AWS SQS | Beta | `LiteBus.Inbox.Ingress.AwsSqs` |
| Kafka | Beta | `LiteBus.Inbox.Ingress.Kafka` |
| InMemory | GA (testing) | `LiteBus.Inbox.Ingress.InMemory` |

Transport platform: `LiteBus.Transport.*`. Kafka transport is GA; AWS SQS and Azure Service Bus transport are Beta.

## Hosting and Operations

| Capability | Doc | Registration |
| --- | --- | --- |
| Manifest (startup, background, diagnostics) | [Hosted services](../architecture/hosted-services.md) | `IModuleConfiguration` |
| Management HTTP | [Operations and management](../operations/README.md) | `LiteBus.Extensions.AspNetCore` |
| Health checks | [Diagnostics and health](../operations/diagnostics-and-health.md) | `LiteBus.Extensions.Diagnostics.HealthChecks` |
| OpenTelemetry | [Architecture](../architecture/README.md) | `*.Extensions.OpenTelemetry` per axis |

## Saga (Extension)

| Capability | Doc | Package |
| --- | --- | --- |
| Correlated inbox state | [Saga](../reliable-messaging/saga.md) | `LiteBus.Saga.InboxIntegration` |

## Analyzers

See [Analyzers](analyzers.md) for LB1001-LB1019. Highlights:

| Rule | Topic |
| --- | --- |
| LB1004 | Command with result stored through inbox (`IInbox.AcceptAsync`, `AcceptBatchAsync`, transactional inbox) |
| LB1007 / LB1017 | Durable contract registration (handled types vs attributed types) |
| LB1014-LB1017 | Processor without dispatcher, transactional EF interceptor, transactional inbox DbContext, explicit contract registration |

## Related Documentation

- [Architecture](../architecture/README.md)
- [Dependency Graph](../architecture/dependency-graph.md)
- [Cookbook and Scenarios](../getting-started/cookbook.md)
- [LiteBus Cheat Sheet](../getting-started/cheat-sheet.md)
