# Feature Index

This index maps each LiteBus capability to its documentation and primary packages. For install commands, see the [Documentation Index](../index.md). For the full cross-axis inventory, see [Capability Catalog](capability-catalog.md).

## Mediator

| Capability | Doc | Packages |
| --- | --- | --- |
| Commands | [Command module](../concepts/commands.md) | `LiteBus.Commands`, `*.Extensions.Microsoft.DependencyInjection` |
| Queries | [Query module](../concepts/queries.md) | `LiteBus.Queries`, `*.Extensions.Microsoft.DependencyInjection` |
| Events | [Event module](../concepts/events.md) | `LiteBus.Events`, `*.Extensions.Microsoft.DependencyInjection` |
| Handler pipeline | [The handler pipeline](../concepts/handler-pipeline.md) | Core mediator packages |
| Guards and shortcuts | [The handler pipeline](../concepts/handler-pipeline.md#deciding-whether-the-work-happens) | Core mediator packages |
| Validation | [Validation](../concepts/validation.md) | Core mediator packages |
| Refusal mapping | [The handler pipeline](../concepts/handler-pipeline.md#refusal-mappers) | Core mediator packages |
| Completion stage | [The handler pipeline](../concepts/handler-pipeline.md#completion-observing-how-mediation-ended) | Core mediator packages |
| Auditing | [Auditing](../concepts/auditing.md) | `LiteBus.Messaging`, axis packages |
| In-process idempotency | [In-process idempotency](../concepts/idempotency.md) | `LiteBus.Messaging`, `LiteBus.Commands` |
| Idempotency outcomes and stores | [In-process idempotency](../concepts/idempotency.md#what-happens-on-each-path) | `LiteBus.Commands.Storage.PostgreSql`, `LiteBus.Commands.Storage.Marten` |
| PostgreSQL idempotency store | [PostgreSQL idempotency store](../integrations/commands-postgresql-idempotency-store.md) | `LiteBus.Commands.Storage.PostgreSql` |
| Marten unit of work, idempotency, and audit | [Marten unit of work, idempotency, and audit](../integrations/commands-marten.md) | `LiteBus.Commands.Storage.Marten` |
| ASP.NET Core Idempotency-Key filter | [ASP.NET Core idempotency key](../integrations/aspnetcore-idempotency-key.md) | `LiteBus.Extensions.AspNetCore` |
| Unit of work (`IUnitOfWork`) | [Domain events and unit of work](../reliable-messaging/domain-events-and-unit-of-work.md#command-pipeline-boundary) | `LiteBus.Messaging.Abstractions`, axis packages |
| Fault refusal classification | [The handler pipeline](../concepts/handler-pipeline.md#turning-faults-into-refusals) | `LiteBus.Messaging`, `LiteBus.Storage.PostgreSql` |
| Shortcut denial | [The handler pipeline](../concepts/handler-pipeline.md#shortcut-contracts) | Core mediator packages |
| Authorization declarations and guards | [Authorization declarations](../concepts/authorization-declarations.md) | `LiteBus.Messaging.Abstractions` |
| Audit selectors | [Auditing](../concepts/auditing.md#declaring-what-the-message-already-knows) | `LiteBus.Messaging` |
| Recurring commands and single-flight lease | [Recurring commands](../integrations/recurring-commands.md) | `LiteBus.Commands.Recurring`, `LiteBus.Commands.Recurring.Storage.PostgreSql` |
| Recording mediator doubles | [Application testing](../testing/application-testing.md#recording-mediator-doubles) | `LiteBus.Testing.Mediation` |
| Message definitions | [Message definitions](../concepts/message-definitions.md) | `LiteBus.Messaging.Abstractions` |
| Required declarations | [Required declarations](../catalog/analyzers/required-declarations.md) | `LiteBus.Messaging`, `LiteBus.Analyzers` |
| Execution context | [Execution context](../concepts/execution-context.md) | `LiteBus.Messaging.Abstractions` |
| Unit-of-work commit position | [Handler priority](../concepts/handler-priority.md#the-reserved-framework-window) | `LiteBus.Messaging.Abstractions` |
| Audit actor attribution | [Auditing](../concepts/auditing.md) | `LiteBus.Messaging`, axis packages |
| Refusal as a value (`TrySendAsync`) | [The handler pipeline](../concepts/handler-pipeline.md#refusal-as-a-value) | Core mediator packages |
| Dry-run evaluation (`EvaluateAsync`) | [The handler pipeline](../concepts/handler-pipeline.md#refusal-as-a-value) | Core mediator packages |
| Mediation telemetry | [The handler pipeline](../concepts/handler-pipeline.md#mediation-telemetry) | `LiteBus.Messaging.Extensions.OpenTelemetry` |
| Pipeline plan (`Explain`) | [The handler pipeline](../concepts/handler-pipeline.md#inspecting-the-plan) | `LiteBus.Messaging` |
| Composition summary | [Getting started](../getting-started/README.md#log-the-composition-summary) | `LiteBus.Messaging` |
| Pipeline test harness | [Testing](../testing/README.md) | `LiteBus.Testing.Mediation` |
| Declaration defaults | [Message definitions](../concepts/message-definitions.md) | `LiteBus.Messaging` |
| Audit catalogue | [Auditing](../concepts/auditing.md) | `LiteBus.Messaging` |
| Custom audit record shape | [Auditing](../concepts/auditing.md) | `LiteBus.Messaging` |

## Durable Messaging Core

| Capability | Doc | Writer API |
| --- | --- | --- |
| Inbox accept + processor | [Inbox](../reliable-messaging/inbox.md) | `IInbox.AcceptAsync(InboxAcceptItem)` |
| Outbox enqueue + processor | [Outbox](../reliable-messaging/outbox.md) | `IOutbox.EnqueueAsync(OutboxEnqueueItem)` |
| Delivery semantics | [Reliable messaging semantics](../reliable-messaging/semantics.md) | Metadata on `*Item` |
| Atomic domain + durable writes | [Transactional messaging writes](../reliable-messaging/transactional-writes.md) | `ITransactionalInbox` / `ITransactionalOutbox` |
| Domain events to outbox | [Domain events and unit of work](../reliable-messaging/domain-events-and-unit-of-work.md) | `OutboxEnqueueMetadata` |
| Domain event staging | [Outbox](../reliable-messaging/outbox.md#domain-event-staging) | `StageCollectedEvents()`, `IDomainEventCollector` |
| Per-handler outbox delivery | [Outbox](../reliable-messaging/outbox.md#per-handler-delivery) | `EnablePerHandlerDelivery()` |
| Ambient trace | [Inbox](../reliable-messaging/inbox.md#ambient-trace-opt-in), [Outbox](../reliable-messaging/outbox.md#ambient-trace-option) | `UseAmbientTrace()` |

## Storage

| Store | Inbox package | Outbox package |
| --- | --- | --- |
| PostgreSQL | `LiteBus.Inbox.Storage.PostgreSql` | `LiteBus.Outbox.Storage.PostgreSql` |
| Entity Framework Core | `LiteBus.Inbox.Storage.EntityFrameworkCore` | `LiteBus.Outbox.Storage.EntityFrameworkCore` |
| InMemory | `LiteBus.Inbox.Storage.InMemory` | `LiteBus.Outbox.Storage.InMemory` |
| Marten | - | `LiteBus.Outbox.Storage.Marten` |

`LiteBus.Storage.PostgreSql` holds the shared PostgreSQL primitives.

The command idempotency stores are `LiteBus.Commands.Storage.PostgreSql` ([PostgreSQL idempotency store](../integrations/commands-postgresql-idempotency-store.md)) and `LiteBus.Commands.Storage.Marten` ([Marten unit of work, idempotency, and audit](../integrations/commands-marten.md)).

## Dispatch

A broker dispatch adapter exists for every transport package. The [Capability Catalog](capability-catalog.md) records the maturity tier for each transport, dispatch, and ingress capability.

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

The transport platform is `LiteBus.Transport.*`. Kafka transport is GA, while AWS SQS and Azure Service Bus transport are Beta.

## Hosting and Operations

| Capability | Doc | Registration |
| --- | --- | --- |
| Manifest (startup, background, diagnostics) | [Hosted services](../architecture/hosted-services.md) | `IModuleConfiguration` |
| Management HTTP | [Operations and management](../operations/management-endpoints.md) | `LiteBus.Extensions.AspNetCore` |
| Health checks | [Diagnostics and health](../operations/diagnostics-and-health.md) | `LiteBus.Extensions.Diagnostics.HealthChecks` |
| OpenTelemetry | [Architecture](../architecture/README.md) | `*.Extensions.OpenTelemetry` per axis, including `LiteBus.Messaging.Extensions.OpenTelemetry` for mediation |

## Saga (Extension)

| Capability | Doc | Package |
| --- | --- | --- |
| Correlated inbox state | [Saga](../reliable-messaging/saga.md) | `LiteBus.Saga.InboxIntegration` |

## Analyzers

See [Analyzers](analyzers.md) for LB1001-LB1024. LB1002 and LB1006 are reserved and never shipped. Highlights:

| Rule | Topic |
| --- | --- |
| LB1004 | Command with result stored through inbox (`IInbox.AcceptAsync`, `AcceptBatchAsync`, transactional inbox) |
| LB1007 / LB1017 | Durable contract registration (handled types vs attributed types) |
| LB1014-LB1017 | Processor without dispatcher, transactional EF interceptor, transactional inbox DbContext, explicit contract registration |
| LB1018 | Message states no audit position |
| LB1019 | Untyped shortcut on a message that produces a result |
| LB1020 / LB1021 | A message states no position on a required declaration, or a required declaration type is not found |
| LB1022 | `[HandlerPriority]` value inside the range LiteBus reserves for its own handlers |
| LB1023 | Command handler sends or publishes another message through a mediator inside its handle method |
| LB1024 | Command handler injects an external side-effect type through its constructor |

## Related Documentation

- [Architecture](../architecture/README.md)
- [Dependency Graph](../architecture/dependency-graph.md)
- [Cookbook and Scenarios](../getting-started/cookbook.md)
- [LiteBus Cheat Sheet](../getting-started/cheat-sheet.md)
